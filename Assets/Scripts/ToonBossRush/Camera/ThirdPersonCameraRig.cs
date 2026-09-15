using UnityEngine;
using UnityEngine.InputSystem;
using ToonBossRush.Player;

namespace ToonBossRush.Camera
{
    /// <summary>
    /// 마우스 오빗(Yaw/Pitch 클램프) + SphereCast 기반 벽 충돌 보정 3인칭 카메라 리그.
    /// New Input System(InputSystem_Actions) 기반: Player/Look 액션(<Pointer>/delta)으로 마우스 델타를 읽음.
    /// 이 스크립트는 Main Camera에 직접 부착해서 사용 — target(플레이어) 주위를 공전하며
    /// 벽에 막히면 SphereCast로 거리 축소.
    /// PlanarForward/PlanarRight를 노출해 PlayerController 등에서 카메라 상대 이동 계산에 사용 가능.
    /// </summary>
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Header("타겟")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);

        [Header("오빗")]
        [SerializeField] private float mouseSensitivity = 2.5f;
        [SerializeField] private float minPitch = -25f;
        [SerializeField] private float maxPitch = 65f;
        [SerializeField] private float startYaw = 0f;
        [SerializeField] private float startPitch = 15f;
        [SerializeField] private bool invertY = false;

        [Header("거리 / 충돌 보정")]
        [SerializeField] private float distance = 3.5f; // 2026-09-15: 카메라 확인 결과 5f는 너무 멀다는 피드백 반영, 더 가깝게 조정
        [SerializeField] private float minDistance = 0.8f;
        [SerializeField] private float collisionRadius = 0.25f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("커서")]
        [SerializeField] private bool lockCursorOnStart = true;

        public Vector3 PlanarForward => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        public Vector3 PlanarRight => Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        private InputSystem_Actions _inputActions;
        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            _inputActions = new InputSystem_Actions();
            _yaw = startYaw;
            _pitch = startPitch;

            if (target == null)
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null)
                {
                    target = tagged.transform;
                }
                else
                {
                    PlayerController playerController = FindFirstObjectByType<PlayerController>();
                    if (playerController != null)
                        target = playerController.transform;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"{nameof(ThirdPersonCameraRig)}: target을 찾지 못했습니다. 캐릭터에 'Player' 태그를 지정하거나 Inspector에서 직접 연결해주세요.", this);
            }
        }

        private void OnEnable()
        {
            _inputActions.Player.Enable();

            if (lockCursorOnStart)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            _inputActions.Player.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            // 테스트 편의용 커서 잠금 토글 (Esc)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                bool nowLocked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = nowLocked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = nowLocked;
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            HandleOrbitInput();
            ApplyOrbitTransform();
        }

        private void HandleOrbitInput()
        {
            Vector2 look = _inputActions.Player.Look.ReadValue<Vector2>();

            _yaw += look.x * mouseSensitivity;
            float pitchDelta = look.y * mouseSensitivity;
            _pitch += invertY ? pitchDelta : -pitchDelta;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void ApplyOrbitTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = target.position + targetOffset;

            float actualDistance = distance;
            Vector3 desiredDir = rotation * Vector3.back; // pivot 기준 카메라 방향(뒤쪽)

            if (Physics.SphereCast(pivot, collisionRadius, desiredDir, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
            {
                actualDistance = Mathf.Clamp(hit.distance, minDistance, distance);
            }

            transform.position = pivot + desiredDir * actualDistance;
            transform.rotation = rotation;
        }
    }
}
