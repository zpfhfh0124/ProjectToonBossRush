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
    /// 2026-10-26: 공격 종료를 고정 타이머(0.4/0.6초) 대신 Animator의 "Attack" 태그 상태가
    /// 끝나는 시점으로 판단하도록 변경 — 공격 클립(Sword And Shield 계열)이 1.5초 이상이라
    /// 타이머로 끊으면 모션 도중 Idle로 튀었음. 약공격은 클립의 comboInputNormalizedTime 이후
    /// 입력이 들어오면 다음 콤보로 바로 캔슬 연결.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        private const string AttackStateTag = "Attack"; // PlayerAnimatorLocomotionBuilder가 공격 상태에 붙이는 태그

        [SerializeField] private Animator animator;
        [SerializeField] private HitboxController lightHitbox;
        [SerializeField] private HitboxController heavyHitbox;
        [SerializeField] private float comboWindowSeconds = 0.8f; // 공격이 끝난 뒤에도 다음 콤보 입력을 받아줄 시간
        [SerializeField, Range(0f, 1f)] private float comboInputNormalizedTime = 0.5f; // 공격 모션의 이 지점 이후부터 다음 약공격으로 캔슬 가능
        [SerializeField] private float attackStateEnterTimeout = 0.3f; // 트리거 후 이 시간 안에 Attack 상태로 못 들어가면 공격 종료 처리(Animator 미구성 대비)
        [SerializeField] private float[] lightComboDamage = { 8f, 8f, 14f };
        [SerializeField] private float heavyDamage = 22f;

        public bool IsAttacking => _isAttacking;

        private PlayerController _controller;
        private InputSystem_Actions _inputActions;
        private int _comboIndex; // 0,1,2
        private float _comboTimer;
        private bool _isAttacking;
        private bool _enteredAttackState;
        private float _attackElapsed;

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
            if (_isAttacking)
            {
                TickAttack();
                return;
            }

            if (_comboTimer > 0f)
            {
                _comboTimer -= Time.deltaTime;
                if (_comboTimer <= 0f) _comboIndex = 0;
            }

            if (!_controller.CanAct) return;

            if (_inputActions.Player.Attack.WasPressedThisFrame()) DoLightAttack();
            else if (_inputActions.Player.HeavyAttack.WasPressedThisFrame()) DoHeavyAttack();
        }

        private void TickAttack()
        {
            _attackElapsed += Time.deltaTime;

            if (animator == null)
            {
                // Animator가 없으면 예전 임시 타이머 방식 그대로
                if (_attackElapsed >= 0.4f) EndAttack();
                return;
            }

            // 크로스페이드 중에는 다음(진입 중인) 상태를 기준으로 판단
            AnimatorStateInfo state = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : animator.GetCurrentAnimatorStateInfo(0);
            bool inAttackState = state.IsTag(AttackStateTag);

            if (!_enteredAttackState)
            {
                if (inAttackState) _enteredAttackState = true;
                else if (_attackElapsed >= attackStateEnterTimeout) EndAttack();
                return;
            }

            if (!inAttackState)
            {
                // 공격 상태의 Exit Time 트랜지션으로 Idle에 돌아옴 → 공격 종료
                EndAttack();
                return;
            }

            // 콤보 캔슬: 약공격 2타째 이후(_comboIndex != 0)만 이어서 연결
            if (_comboIndex != 0 && state.normalizedTime >= comboInputNormalizedTime
                && _inputActions.Player.Attack.WasPressedThisFrame())
            {
                CancelPendingHitboxInvokes();
                DoLightAttack();
            }
        }

        private void DoLightAttack()
        {
            BeginAttack();
            if (lightHitbox != null)
                lightHitbox.Damage = lightComboDamage[_comboIndex];
            if (animator != null)
            {
                animator.SetInteger("ComboIndex", _comboIndex);
                animator.SetTrigger("LightAttack");
            }

            _comboIndex = (_comboIndex + 1) % lightComboDamage.Length;
            _comboTimer = comboWindowSeconds;

            // 2026-09-21 추가: 히트박스 on/off도 원래는 애니메이션 이벤트(Activate/Deactivate)로
            // 연결하는 게 최종 형태 — 아직 공격 클립에 이벤트가 없어서 임시 타이머로 대신 켜고 끔.
            // 클립 이벤트를 연결하면 아래 Invoke 2줄은 지우고
            // 클립 쪽에서 lightHitbox.Activate()/Deactivate()를 직접 호출하도록 교체할 것.
            if (lightHitbox != null)
            {
                Invoke(nameof(ActivateLightHitbox), 0.25f);
                Invoke(nameof(DeactivateLightHitbox), 0.5f);
            }
        }

        private void DoHeavyAttack()
        {
            BeginAttack();
            if (heavyHitbox != null)
                heavyHitbox.Damage = heavyDamage;
            if (animator != null) animator.SetTrigger("HeavyAttack");
            _comboIndex = 0;

            // 2026-09-21 추가: DoLightAttack과 동일한 임시 처리(위 주석 참고)
            if (heavyHitbox != null)
            {
                Invoke(nameof(ActivateHeavyHitbox), 0.4f);
                Invoke(nameof(DeactivateHeavyHitbox), 0.8f);
            }
        }

        private void BeginAttack()
        {
            _isAttacking = true;
            _enteredAttackState = false;
            _attackElapsed = 0f;
            // PlayerController가 이번 프레임에 먼저 Update됐다면 ActionLocked가 아직 false라
            // 같은 프레임에 Running/Idle 트랜지션이 공격 트리거와 경합할 수 있음 → 여기서 즉시 잠금
            if (animator != null) animator.SetBool("ActionLocked", true);
        }

        private void CancelPendingHitboxInvokes()
        {
            CancelInvoke(nameof(ActivateLightHitbox));
            CancelInvoke(nameof(DeactivateLightHitbox));
            if (lightHitbox != null) lightHitbox.Deactivate();
        }

        private void ActivateLightHitbox() => lightHitbox.Activate();
        private void DeactivateLightHitbox() => lightHitbox.Deactivate();
        private void ActivateHeavyHitbox() => heavyHitbox.Activate();
        private void DeactivateHeavyHitbox() => heavyHitbox.Deactivate();

        /// <summary>
        /// 공격 상태 종료 시 자동 호출됨. 애니메이션 이벤트(공격 클립 마지막 프레임)에서 직접 호출해도 무방.
        /// </summary>
        public void EndAttack()
        {
            _isAttacking = false;
            _comboTimer = comboWindowSeconds;
        }
    }
}
