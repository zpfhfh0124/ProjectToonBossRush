using UnityEngine;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 카메라 상대 8방향 이동 + 회피(구르기, 짧은 무적프레임).
    /// 빠른 시작을 위해 레거시 Input(Input.GetAxis)을 사용 — 필요하면 New Input System으로 교체.
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
        private Vector3 _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
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
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v);

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
            if (!Input.GetButtonDown("Jump") && !Input.GetKeyDown(KeyCode.LeftShift)) return; // 임시 바인딩

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v);
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
