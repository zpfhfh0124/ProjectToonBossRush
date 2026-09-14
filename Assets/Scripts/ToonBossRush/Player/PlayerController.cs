using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 720f; // deg/sec
        [SerializeField] private float gravity = -20f;

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
        private InputSystem_Actions _inputActions;
        private Vector3 _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
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
                animator?.SetFloat("MoveSpeed", 0f);
                return;
            }

            inputDir.Normalize();

            // 카메라 기준 상대 방향으로 변환
            Vector3 camForward = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 camRight = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized : Vector3.right;
            Vector3 moveDir = (camForward * inputDir.z + camRight * inputDir.x).normalized;

            _controller.Move(moveDir * moveSpeed * Time.deltaTime);

            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

            animator?.SetFloat("MoveSpeed", moveDir.magnitude);
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
            animator?.SetTrigger("Dodge");
        }

        private void TickDodge()
        {
            _dodgeTimer += Time.deltaTime;
            IsInvincible = _dodgeTimer <= dodgeInvincibleWindow;

            float t = Mathf.Clamp01(_dodgeTimer / dodgeDuration);
            // ease-out 느낌으로 구르는 속도 감소
            float speedMul = 1f - t;
            _controller.Move(_dodgeDirection * (dodgeDistance / dodgeDuration) * speedMul * Time.deltaTime);

            if (_dodgeTimer >= dodgeDuration)
            {
                IsDodging = false;
                IsInvincible = false;
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
    }
}
