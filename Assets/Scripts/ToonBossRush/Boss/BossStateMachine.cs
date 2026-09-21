using System.Collections.Generic;
using UnityEngine;
using ToonBossRush.Combat;

namespace ToonBossRush.Boss
{
    public enum BossState { Idle, Telegraph, Attack, Recover, Stagger, Dead }

    /// <summary>
    /// 보스 공용 상태머신. Idle -> Telegraph -> Attack -> Recover -> Idle 루프,
    /// 스태거 임계치를 넘으면 언제든 Stagger로 전환.
    /// UE5 포트폴리오(Ai.Mi, 리캐스트 스테이트머신)의 설계를 Unity/C#으로 재구현한 버전.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class BossStateMachine : MonoBehaviour
    {
        [SerializeField] private List<BossAttackPatternSO> patterns;
        [SerializeField] private float idleDurationBeforeAttack = 1.0f;
        [SerializeField] private float staggerDuration = 2.0f;
        [SerializeField] private Animator animator;
        [SerializeField] private HitboxController attackHitbox;

        public BossState CurrentState { get; private set; } = BossState.Idle;

        // 2026-09-20: GameFlowManager가 "이 보스가 죽으면 다음 인카운터로 넘어간다"를
        // 판단하려면 보스의 Health에 접근해야 하는데, 지금까지는 이 컴포넌트 안에서만
        // private으로 들고 있었음 — 게임 플로우 상위 로직에서 구독할 수 있도록 공개.
        public Health Health => _health;

        private Health _health;
        private float _stateTimer;
        private BossAttackPatternSO _currentPattern;
        private readonly Dictionary<BossAttackPatternSO, float> _lastUsedTime = new Dictionary<BossAttackPatternSO, float>();

        private void Awake()
        {
            _health = GetComponent<Health>();
            _health.OnStaggered += HandleStaggered;
            _health.OnDied += HandleDied;
        }

        private void Update()
        {
            if (CurrentState == BossState.Dead) return;

            _stateTimer -= Time.deltaTime;
            if (_stateTimer > 0f) return;

            switch (CurrentState)
            {
                case BossState.Idle:
                    EnterTelegraph();
                    break;
                case BossState.Telegraph:
                    EnterAttack();
                    break;
                case BossState.Attack:
                    EnterRecover();
                    break;
                case BossState.Recover:
                    EnterIdle();
                    break;
                case BossState.Stagger:
                    EnterIdle();
                    break;
            }
        }

        /// <summary>
        /// GameFlowManager가 이 보스의 인카운터를 시작시킬 때 호출.
        /// 지금은 EnterIdle()과 동일하지만, 나중에 "보스 등장" 연출(카메라 팬, 등장 애니메이션 트리거 등)을
        /// 붙일 자리로 이 메서드를 따로 유지한다 — 호출부(GameFlowManager)를 바꿀 필요 없이 내부만 확장 가능.
        /// </summary>
        public void BeginEncounter()
        {
            EnterIdle();
        }

        /// <summary>
        /// 재도전(Retry)/런 재시작 시 체력·상태·패턴 재사용 기록을 전부 초기 상태로 되돌린다.
        /// GameFlowManager.StartRun()에서 모든 인카운터에 대해 한 번씩 호출.
        /// </summary>
        public void ResetEncounter()
        {
            _health.ResetHealth();
            _lastUsedTime.Clear();
            _currentPattern = null;
            _stateTimer = idleDurationBeforeAttack;
            CurrentState = BossState.Idle;
            if (attackHitbox != null) attackHitbox.Deactivate();
        }

        private void EnterIdle()
        {
            CurrentState = BossState.Idle;
            _stateTimer = idleDurationBeforeAttack;
            animator?.SetTrigger("Idle");
        }

        private void EnterTelegraph()
        {
            _currentPattern = PickPattern();
            if (_currentPattern == null)
            {
                // 사용 가능한 패턴이 없으면(전부 쿨다운) 잠깐 더 대기
                _stateTimer = 0.5f;
                return;
            }

            CurrentState = BossState.Telegraph;
            _stateTimer = _currentPattern.telegraphDuration;
            animator?.SetTrigger("Telegraph");
            // TODO: 여기서 텔레그래프 VFX(경고 링, 색 변화 등)를 트리거하면
            // 플레이어가 회피 타이밍을 읽을 수 있는 정보를 준다.
        }

        private void EnterAttack()
        {
            CurrentState = BossState.Attack;
            _stateTimer = _currentPattern.attackDuration;
            if (attackHitbox != null) attackHitbox.Damage = _currentPattern.damage;
            animator?.SetTrigger(_currentPattern.animatorTrigger);
            _lastUsedTime[_currentPattern] = Time.time;
            // 실제 히트박스 on/off는 애니메이션 이벤트 -> HitboxController.Activate/Deactivate 로 연결
        }

        private void EnterRecover()
        {
            CurrentState = BossState.Recover;
            _stateTimer = _currentPattern.recoverDuration;
            animator?.SetTrigger("Recover");
        }

        private void HandleStaggered()
        {
            if (CurrentState == BossState.Dead) return;
            CurrentState = BossState.Stagger;
            _stateTimer = staggerDuration;
            animator?.SetTrigger("Stagger");
        }

        private void HandleDied()
        {
            CurrentState = BossState.Dead;
            animator?.SetTrigger("Dead");
            if (attackHitbox != null) attackHitbox.Deactivate();
        }

        private BossAttackPatternSO PickPattern()
        {
            if (patterns == null || patterns.Count == 0) return null;

            List<BossAttackPatternSO> available = new List<BossAttackPatternSO>();
            foreach (var p in patterns)
            {
                float lastUsed = _lastUsedTime.TryGetValue(p, out var t) ? t : -999f;
                if (Time.time - lastUsed >= p.minReuseInterval) available.Add(p);
            }
            if (available.Count == 0) available = patterns;

            return available[Random.Range(0, available.Count)];
        }
    }
}
