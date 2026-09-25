using UnityEngine;
using UnityEngine.InputSystem;
using ToonBossRush.Combat;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 카메라 상대 8방향 이동 + 회피(구르기, 짧은 무적프레임).
    /// New Input System(InputSystem_Actions) 기반. Move는 Player/Move(Vector2),
    /// 회피는 Player/Dodge(Button, LeftShift/Space 바인딩) 액션을 사용.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("이동")]
        // 2026-09-18: 5 → 1.8로 낮춤. moveSpeed가 referenceWalkClipSpeed(1.5)보다
        // 훨씬 커서 AnimSpeedMultiplier가 3.33배까지 올라가 다리가 빠르게 감기는(너무 빨리 걷는)
        // 것처럼 보였음. 1.8이면 배율이 1.2배 정도로 줄어 눈에 거의 안 띔("속도 정상화").
        [SerializeField] private float moveSpeed = 1.8f;
        [SerializeField] private float rotationSpeed = 720f; // deg/sec
        [SerializeField] private float gravity = -20f;

        [Header("애니메이션 동기화")]
        // Walk 클립이 "제자리에서" 표현하는 보폭 속도(m/s) 추정치.
        // CharacterController는 moveSpeed로 즉시 이동하는데 클립은 이 값과 무관하게
        // 자체 재생 속도(1배)로 도는 게 기본이라, moveSpeed와 이 값이 다르면
        // 발이 실제 이동보다 늦게/빠르게 나가는(미끄러지는) 것처럼 보임.
        // Walking/Turn/Strafe 상태의 Speed Parameter를 AnimSpeedMultiplier로 연결해두면
        // 이 값이 moveSpeed / referenceWalkClipSpeed 배율로 클립 재생 속도를 보정함.
        // 발이 계속 밀리면(체공감) 이 값을 낮추고, 반대로 발이 헛돌면(제자리걸음) 값을 높일 것.
        [SerializeField] private float referenceWalkClipSpeed = 1.5f;

        [Header("회피")]
        [SerializeField] private float dodgeDistance = 4f;
        [SerializeField] private float dodgeDuration = 0.35f;
        [SerializeField] private float dodgeInvincibleWindow = 0.25f; // dodgeDuration 이내여야 함
        [SerializeField] private float dodgeCooldown = 0.6f;

        [Header("참조")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Animator animator;

        public bool IsInvincible { get; private set; }
        public bool IsDodging { get; private set; }
        public bool CanAct => !IsDodging; // 공격 시스템에서 참조

        private CharacterController _controller;
        private Health _health; // 2026-09-21 추가: 회피 무적 프레임을 Health.IsInvincible에 동기화하기 위함(선택적 — 없으면 무시)
        private InputSystem_Actions _inputActions;
        private Vector3 _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;
        private bool _wasMoving; // 직전 프레임에 이동 입력이 있었는지 (정지→이동 전환 감지용)

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _inputActions = new InputSystem_Actions();

            if (cameraTransform == null && UnityEngine.Camera.main != null)
                cameraTransform = UnityEngine.Camera.main.transform;

            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            _inputActions.Player.Enable();
        }

        private void OnDisable()
        {
            _inputActions.Player.Disable();
        }

        private void Update()
        {
            _dodgeCooldownTimer -= Time.deltaTime;

            if (IsDodging)
            {
                TickDodge();
                return;
            }

            HandleMoveAndRotate();
            HandleDodgeInput();
            ApplyGravity();
        }

        private void HandleMoveAndRotate()
        {
            Vector2 moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);

            if (inputDir.sqrMagnitude < 0.001f)
            {
                _wasMoving = false;
                if (animator != null)
                {
                    animator.SetFloat("MoveSpeed", 0f);
                    animator.SetFloat("TurnAngle", 0f);
                    animator.SetFloat("AnimSpeedMultiplier", 1f);
                }
                return;
            }

            inputDir.Normalize();

            // 카메라 기준 상대 방향으로 변환
            Vector3 camForward = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 camRight = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized : Vector3.right;
            Vector3 moveDir = (camForward * inputDir.z + camRight * inputDir.x).normalized;

            float signedTurnAngle;

            if (!_wasMoving)
            {
                // 정지(Idle) 상태에서 막 이동을 시작하는 첫 프레임.
                // Idle 때 우연히 보고 있던 transform.forward와 새 moveDir 사이의
                // 오차가 그대로 TurnAngle로 들어가면, 직선으로 걷기 시작했을 뿐인데도
                // 그 오차가 35도(TurnThreshold)를 넘어 TurnRight/TurnLeft 애니메이션이
                // 한 프레임 잘못 트리거되는 문제가 있었음(2026-09-20 확인).
                // → 이동 시작 순간에는 회전을 점진적으로 따라잡지 않고 moveDir로 즉시
                // 정렬해서 오차각 자체가 발생하지 않도록 함.
                transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
                signedTurnAngle = 0f;
                _wasMoving = true;
            }
            else
            {
                // 회전이 moveDir을 따라잡기 전, "몸이 향한 방향"과 "실제 이동 방향"의 오차각.
                // rotationSpeed로 인해 방향을 급하게 꺾을수록 이 값이 순간적으로 커졌다가
                // 몸이 회전을 따라잡으며 매 프레임 자연스럽게 0으로 수렴함 — 이 수렴 과정을
                // Turn/Strafe 애니메이션 전환의 트리거로 사용(양수=오른쪽으로 꺾어야 함).
                signedTurnAngle = Vector3.SignedAngle(transform.forward, moveDir, Vector3.up);

                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            _controller.Move(moveDir * moveSpeed * Time.deltaTime);

            // 실제 이동 속도(moveSpeed)와 클립이 표현하는 보폭 속도(referenceWalkClipSpeed)의
            // 비율로 클립 재생 속도를 보정 — Animator 쪽 상태의 Speed Parameter가
            // AnimSpeedMultiplier로 연결돼 있어야 실제로 적용됨(빌더 스크립트 참고).
            float animSpeedMultiplier = referenceWalkClipSpeed > 0.001f ? moveSpeed / referenceWalkClipSpeed : 1f;
            if (animator != null)
            {
                animator.SetFloat("MoveSpeed", moveDir.magnitude);
                animator.SetFloat("TurnAngle", signedTurnAngle);
                animator.SetFloat("AnimSpeedMultiplier", animSpeedMultiplier);
            }
        }

        private void HandleDodgeInput()
        {
            if (_dodgeCooldownTimer > 0f) return;
            if (!_inputActions.Player.Dodge.WasPressedThisFrame()) return;

            Vector2 moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
            Vector3 camForward = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 camRight = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized : Vector3.right;

            _dodgeDirection = inputDir.sqrMagnitude > 0.001f
                ? (camForward * inputDir.z + camRight * inputDir.x).normalized
                : transform.forward;

            IsDodging = true;
            _dodgeTimer = 0f;
            _dodgeCooldownTimer = dodgeCooldown;
            if (animator != null) animator.SetTrigger("Dodge");
        }

        private void TickDodge()
        {
            _dodgeTimer += Time.deltaTime;
            IsInvincible = _dodgeTimer <= dodgeInvincibleWindow;
            if (_health != null) _health.IsInvincible = IsInvincible;

            float t = Mathf.Clamp01(_dodgeTimer / dodgeDuration);
            // ease-out 느낌으로 구르는 속도 감소
            float speedMul = 1f - t;
            _controller.Move(_dodgeDirection * (dodgeDistance / dodgeDuration) * speedMul * Time.deltaTime);

            if (_dodgeTimer >= dodgeDuration)
            {
                IsDodging = false;
                IsInvincible = false;
                if (_health != null) _health.IsInvincible = false;
            }
        }

        private void ApplyGravity()
        {
            if (_controller.isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -2f;
            }
            _verticalVelocity.y += gravity * Time.deltaTime;
            _controller.Move(_verticalVelocity * Time.deltaTime);
        }

        /// <summary>
        /// 2026-09-20 추가: 게임 플로우(런 시작/재도전)에서 플레이어를 스폰 지점으로
        /// 안전하게 옮길 때 사용. CharacterController가 켜진 상태에서 transform을 직접
        /// 건드리면 다음 프레임 충돌 보정이 어긋날 수 있어, 잠깐 비활성화한 뒤 옮기고
        /// 다시 켠다. 회피/낙하 등 이동 관련 내부 상태도 함께 초기화해서, 예를 들어
        /// 회피 중 사망 → 재도전으로 이어지는 경우에도 이상한 잔여 상태 없이 시작하게 한다.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            IsDodging = false;
            IsInvincible = false;
            if (_health != null) _health.IsInvincible = false;
            _wasMoving = false;
            _verticalVelocity = Vector3.zero;

            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
        }
    }
}
