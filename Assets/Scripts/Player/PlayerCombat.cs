using UnityEngine;
using ToonBossRush.Combat;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 약공격 3단 콤보 + 강공격 1타.
    /// 실제 히트박스 on/off는 애니메이션 이벤트가 HitboxController.Activate/Deactivate를 호출하는 구조.
    /// (여기서는 콤보 타이밍/입력 버퍼만 담당)
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
        private int _comboIndex; // 0,1,2
        private float _comboTimer;
        private bool _isAttacking;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (_comboTimer > 0f)
            {
                _comboTimer -= Time.deltaTime;
                if (_comboTimer <= 0f) _comboIndex = 0;
            }

            if (!_controller.CanAct || _isAttacking) return;

            if (Input.GetMouseButtonDown(0)) DoLightAttack();
            else if (Input.GetMouseButtonDown(1)) DoHeavyAttack();
        }

        private void DoLightAttack()
        {
            _isAttacking = true;
            lightHitbox.Damage = lightComboDamage[_comboIndex];
            animator?.SetInteger("ComboIndex", _comboIndex);
            animator?.SetTrigger("LightAttack");

            _comboIndex = (_comboIndex + 1) % lightComboDamage.Length;
            _comboTimer = comboWindowSeconds;

            // 실제로는 애니메이션 이벤트로 EndAttack()을 호출해야 함(README 참고).
            // 클립이 아직 없다면 아래 임시 코루틴으로 테스트 가능.
            Invoke(nameof(EndAttack), 0.4f);
        }

        private void DoHeavyAttack()
        {
            _isAttacking = true;
            heavyHitbox.Damage = heavyDamage;
            animator?.SetTrigger("HeavyAttack");
            _comboIndex = 0;

            Invoke(nameof(EndAttack), 0.6f);
        }

        /// <summary>애니메이션 이벤트(공격 클립 마지막 프레임)에서 호출하도록 연결 권장</summary>
        public void EndAttack()
        {
            _isAttacking = false;
        }
    }
}
