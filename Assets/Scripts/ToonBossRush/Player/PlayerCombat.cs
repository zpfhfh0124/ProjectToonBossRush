using UnityEngine;
using UnityEngine.InputSystem;
using ToonBossRush.Combat;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 약공격 3단 콤보 + 강공격 1타.
    /// 실제 히트박스 on/off는 애니메이션 이벤트가 HitboxController.Activate/Deactivate를 호출하는 구조.
    /// (여기서는 콤보 타이밍/입력 버퍼만 담당)
    /// New Input System(InputSystem_Actions) 기반: 약공격은 Player/Attack(좌클릭),
    /// 강공격은 Player/HeavyAttack(우클릭) 액션을 사용.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private HitboxController lightHitbox;
        [SerializeField] private HitboxController heavyHitbox;
        [SerializeField] private float comboWindowSeconds = 0.8f; // 다음 입력을 받아줄 시간
        [SerializeField] private float[] lightComboDamage = { 8f, 8f, 14f };
        [SerializeField] private float heavyDamage = 22f;

        private PlayerController _controller;
        private InputSystem_Actions _inputActions;
        private int _comboIndex; // 0,1,2
        private float _comboTimer;
        private bool _isAttacking;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _inputActions = new InputSystem_Actions();

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
            if (_comboTimer > 0f)
            {
                _comboTimer -= Time.deltaTime;
                if (_comboTimer <= 0f) _comboIndex = 0;
            }

            if (!_controller.CanAct || _isAttacking) return;

            if (_inputActions.Player.Attack.WasPressedThisFrame()) DoLightAttack();
            else if (_inputActions.Player.HeavyAttack.WasPressedThisFrame()) DoHeavyAttack();
        }

        private void DoLightAttack()
        {
            _isAttacking = true;
            if (lightHitbox != null)
                lightHitbox.Damage = lightComboDamage[_comboIndex];
            if (animator != null)
            {
                animator.SetInteger("ComboIndex", _comboIndex);
                animator.SetTrigger("LightAttack");
            }

            _comboIndex = (_comboIndex + 1) % lightComboDamage.Length;
            _comboTimer = comboWindowSeconds;

            // 실제로는 애니메이션 이벤트로 EndAttack()을 호출해야 함(README 참고).
            // 클립이 아직 없다면 아래 임시 코루틴으로 테스트 가능.
            Invoke(nameof(EndAttack), 0.4f);

            // 2026-09-21 추가: 히트박스 on/off도 원래는 애니메이션 이벤트(Activate/Deactivate)로
            // 연결하는 게 최종 형태 — 아직 공격 클립에 이벤트가 없어서, 위 EndAttack과 같은 방식으로
            // 임시 타이머로 대신 켜고 끔. 클립 이벤트를 연결하면 아래 Invoke 2줄은 지우고
            // 클립 쪽에서 lightHitbox.Activate()/Deactivate()를 직접 호출하도록 교체할 것.
            if (lightHitbox != null)
            {
                Invoke(nameof(ActivateLightHitbox), 0.1f);
                Invoke(nameof(DeactivateLightHitbox), 0.3f);
            }
        }

        private void DoHeavyAttack()
        {
            _isAttacking = true;
            if (heavyHitbox != null)
                heavyHitbox.Damage = heavyDamage;
            if (animator != null) animator.SetTrigger("HeavyAttack");
            _comboIndex = 0;

            Invoke(nameof(EndAttack), 0.6f);

            // 2026-09-21 추가: DoLightAttack과 동일한 임시 처리(위 주석 참고)
            if (heavyHitbox != null)
            {
                Invoke(nameof(ActivateHeavyHitbox), 0.15f);
                Invoke(nameof(DeactivateHeavyHitbox), 0.45f);
            }
        }

        private void ActivateLightHitbox() => lightHitbox.Activate();
        private void DeactivateLightHitbox() => lightHitbox.Deactivate();
        private void ActivateHeavyHitbox() => heavyHitbox.Activate();
        private void DeactivateHeavyHitbox() => heavyHitbox.Deactivate();

        /// <summary>애니메이션 이벤트(공격 클립 마지막 프레임)에서 호출하도록 연결 권장</summary>
        public void EndAttack()
        {
            _isAttacking = false;
        }
    }
}
