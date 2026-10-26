using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using ToonBossRush.Combat;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 카메라 상대 8방향 이동(기본 달리기 / Walk 키 누르는 동안 걷기) + 점프 + 회피(구르기, 짧은 무적프레임).
    /// New Input System(InputSystem_Actions) 기반. Move는 Player/Move(Vector2),
    /// 걷기는 Player/Walk(LeftCtrl), 점프는 Player/Jump(Space),
    /// 회피는 Player/Dodge(Button, LeftShift 바인딩) 액션을 사용.
    /// 2026-10-26: 게임패드는 스틱을 살짝만 기울이면(gamepadWalkThreshold 미만) 걷기로 처리.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // Animator MoveSpeed 파라미터 값 — PlayerAnimatorLocomotionBuilder의 임계값(0.1 / 0.75)과 짝을 이룸
        private const float AnimMoveSpeedWalk = 0.5f;
        private const float AnimMoveSpeedRun = 1f;

        [Header("이동")]
        // 2026-10-26: 기본 이동을 달리기로 변경. 기존 moveSpeed(1.8)는 "천천히 걷기" 속도로 그대로 이어받음
        // (씬에 저장된 값을 잃지 않도록 FormerlySerializedAs).
        [SerializeField] private float runSpeed = 5.5f;
        [FormerlySerializedAs("moveSpeed")]
        [SerializeField] private float walkSpeed = 1.8f;
        [SerializeField, Range(0.1f, 0.9f)] private float gamepadWalkThreshold = 0.5f; // 스틱 기울기가 이 값 미만이면 걷기
        [SerializeField] private float rotationSpeed = 720f; // deg/sec
        [SerializeField] private float gravity = -20f;

        [Header("점프")]
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.6f; // 공중에서 이동 속도 배율

        [Header("애니메이션 동기화")]
        // Walk/Run 클립이 "제자리에서" 표현하는 보폭 속도(m/s) 추정치.
        // CharacterController는 walkSpeed/runSpeed로 즉시 이동하는데 클립은 이 값과 무관하게
        // 자체 재생 속도(1배)로 도는 게 기본이라, 실제 속도와 이 값이 다르면
        // 발이 실제 이동보다 늦게/빠르게 나가는(미끄러지는) 것처럼 보임.
        // 이동 상태의 Speed Parameter를 AnimSpeedMultiplier로 연결해두면
        // 이 값이 (실제 속도 / 기준 속도) 배율로 클립 재생 속도를 보정함.
        // 발이 계속 밀리면(체공감) 이 값을 낮추고, 반대로 발이 헛돌면(제자리걸음) 값을 높일 것.
        [SerializeField] private float referenceWalkClipSpeed = 1.5f;
        [SerializeField] private float referenceRunClipSpeed = 5f;

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
        public bool IsGrounded { get; private set; } = true;
        public bool IsWalking { get; private set; }
        public bool CanAct => !IsDodging && IsGrounded; // 공격 시스템에서 참조

        private CharacterController _controller;
        private Health _health; // 2026-09-21 추가: 회피 무적 프레임을 Health.IsInvincible에 동기화하기 위함(선택적 — 없으면 무시)
        private PlayerCombat _combat; // 2026-10-26 추가: 공격 중 이동/회전 잠금용(선택적 — 없으면 무시)
        private InputSystem_Actions _inputActions;
        private Vector3 _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;
        private bool _wasMoving; // 직전 프레임에 이동 입력이 있었는지 (정지→이동 전환 감지용)

        private bool IsAttacking => _combat != null && _combat.IsAttacking;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _combat = GetComponent<PlayerCombat>();
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
            }
            else
            {
                HandleMoveAndRotate();
                HandleJumpInput();
                HandleDodgeInput();
                ApplyGravity();
            }

            UpdateAnimatorState();
        }

        private void HandleMoveAndRotate()
        {
            Vector2 moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);

            // 공격 중에는 제자리에서 모션을 끝까지 보여줌(이동/회전 잠금)
            if (inputDir.sqrMagnitude < 0.001f || IsAttacking)
            {
                _wasMoving = false;
                IsWalking = false;
                if (animator != null)
                {
                    animator.SetFloat("MoveSpeed", 0f);
                    animator.SetFloat("TurnAngle", 0f);
                    animator.SetFloat("AnimSpeedMultiplier", 1f);
                }
                return;
            }

            // 키보드는 Walk 키, 게임패드는 스틱을 살짝만 기울였을 때 걷기
            IsWalking = _inputActions.Player.Walk.IsPressed() || moveInput.magnitude < gamepadWalkThreshold;
            float speed = IsWalking ? walkSpeed : runSpeed;
            float referenceClipSpeed = IsWalking ? referenceWalkClipSpeed : referenceRunClipSpeed;

            inputDir.Normalize();
            Vector3 moveDir = ToCameraRelative(inputDir);

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

            if (!IsGrounded) speed *= airControl;
            _controller.Move(moveDir * speed * Time.deltaTime);

            // 실제 이동 속도와 클립이 표현하는 보폭 속도의 비율로 클립 재생 속도를 보정 —
            // Animator 쪽 상태의 Speed Parameter가 AnimSpeedMultiplier로 연결돼 있어야
            // 실제로 적용됨(빌더 스크립트 참고).
            float animSpeedMultiplier = referenceClipSpeed > 0.001f ? speed / referenceClipSpeed : 1f;
            if (animator != null)
            {
                animator.SetFloat("MoveSpeed", IsWalking ? AnimMoveSpeedWalk : AnimMoveSpeedRun);
                animator.SetFloat("TurnAngle", signedTurnAngle);
                animator.SetFloat("AnimSpeedMultiplier", animSpeedMultiplier);
            }
        }

        private void HandleJumpInput()
        {
            if (!IsGrounded || IsAttacking) return;
            if (!_inputActions.Player.Jump.WasPressedThisFrame()) return;

            // v = sqrt(2gh) — gravity가 음수라 부호 반전
            _verticalVelocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
            IsGrounded = false;
            if (animator != null) animator.SetTrigger("Jump");
        }

        private void HandleDodgeInput()
        {
            if (_dodgeCooldownTimer > 0f || !IsGrounded || IsAttacking) return;
            if (!_inputActions.Player.Dodge.WasPressedThisFrame()) return;

            Vector2 moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);

            _dodgeDirection = inputDir.sqrMagnitude > 0.001f
                ? ToCameraRelative(inputDir)
                : transform.forward;
            transform.rotation = Quaternion.LookRotation(_dodgeDirection, Vector3.up); // 구르는 방향으로 몸을 돌려 롤 모션과 일치시킴

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
            IsGrounded = _controller.isGrounded;
        }

        /// <summary>
        /// 이동 계열 AnyState 트랜지션은 ActionLocked가 false일 때만 동작(빌더 참고) —
        /// 안 그러면 공격/회피/점프 모션이 시작되자마자 MoveSpeed 조건에 의해 Idle/Run으로 덮어써짐.
        /// </summary>
        private void UpdateAnimatorState()
        {
            if (animator == null) return;
            animator.SetBool("IsGrounded", IsGrounded);
            animator.SetBool("ActionLocked", IsDodging || IsAttacking || !IsGrounded);
        }

        private Vector3 ToCameraRelative(Vector3 inputDir)
        {
            Vector3 camForward = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 camRight = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized : Vector3.right;
            return (camForward * inputDir.z + camRight * inputDir.x).normalized;
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
            IsGrounded = true;

            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
        }
    }
}
