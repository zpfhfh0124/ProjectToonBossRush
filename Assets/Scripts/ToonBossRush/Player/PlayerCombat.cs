using UnityEngine;
using UnityEngine.InputSystem;
using ToonBossRush.Combat;

namespace ToonBossRush.Player
{
    /// <summary>
    /// 약공격 3단 콤보 + 강공격 1타.
    /// 실제 히트박스 on/off는 공격 클립의 애니메이션 이벤트(AttackHitboxOn/AttackHitboxOff)가 담당.
    /// 이벤트는 Animator가 붙은 오브젝트로만 전달되므로 이 컴포넌트는 Animator와 같은 오브젝트에 있어야 함.
    /// (이벤트는 PlayerAnimatorLocomotionBuilder가 임포트 설정에 심음)
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
        private HitboxController _activeHitbox; // 현재 공격의 히트박스 — 약/강공격 클립이 같은 이벤트 이름을 공유

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
                AttackHitboxOff(); // 캔슬되면 이전 클립의 Off 이벤트를 보장할 수 없음
                DoLightAttack();
            }
        }

        private void DoLightAttack()
        {
            BeginAttack(lightHitbox);
            if (lightHitbox != null)
                lightHitbox.Damage = lightComboDamage[_comboIndex];
            if (animator != null)
            {
                animator.SetInteger("ComboIndex", _comboIndex);
                animator.SetTrigger("LightAttack");
            }

            _comboIndex = (_comboIndex + 1) % lightComboDamage.Length;
            _comboTimer = comboWindowSeconds;
        }

        private void DoHeavyAttack()
        {
            BeginAttack(heavyHitbox);
            if (heavyHitbox != null)
                heavyHitbox.Damage = heavyDamage;
            if (animator != null) animator.SetTrigger("HeavyAttack");
            _comboIndex = 0;
        }

        private void BeginAttack(HitboxController hitbox)
        {
            _activeHitbox = hitbox;
            _isAttacking = true;
            _enteredAttackState = false;
            _attackElapsed = 0f;
            // PlayerController가 이번 프레임에 먼저 Update됐다면 ActionLocked가 아직 false라
            // 같은 프레임에 Running/Idle 트랜지션이 공격 트리거와 경합할 수 있음 → 여기서 즉시 잠금
            if (animator != null) animator.SetBool("ActionLocked", true);
        }

        /// <summary>공격 클립의 애니메이션 이벤트에서 호출 — 타격 구간 시작</summary>
        public void AttackHitboxOn()
        {
            if (_isAttacking && _activeHitbox != null) _activeHitbox.Activate();
        }

        /// <summary>공격 클립의 애니메이션 이벤트에서 호출 — 타격 구간 끝</summary>
        public void AttackHitboxOff()
        {
            if (_activeHitbox != null) _activeHitbox.Deactivate();
        }

        /// <summary>
        /// 공격 상태 종료 시 자동 호출됨. 애니메이션 이벤트(공격 클립 마지막 프레임)에서 직접 호출해도 무방.
        /// </summary>
        public void EndAttack()
        {
            AttackHitboxOff(); // 공격이 도중에 끊겨 Off 이벤트가 오지 않아도 히트박스가 켜진 채 남지 않게
            _isAttacking = false;
            _comboTimer = comboWindowSeconds;
        }

        /// <summary>
        /// 재도전 등으로 플레이어를 리셋할 때 호출(PlayerController.Teleport에서 호출됨).
        /// 사망 시점에 공격 중이었다면 _isAttacking이 남아 재도전 후 첫 입력이 무시될 수 있어서 추가.
        /// </summary>
        public void ResetCombatState()
        {
            AttackHitboxOff();
            _activeHitbox = null;
            _isAttacking = false;
            _enteredAttackState = false;
            _attackElapsed = 0f;
            _comboIndex = 0;
            _comboTimer = 0f;

            if (animator != null)
            {
                animator.ResetTrigger("LightAttack");
                animator.ResetTrigger("HeavyAttack");
                animator.SetInteger("ComboIndex", 0);
            }
        }
    }
}
