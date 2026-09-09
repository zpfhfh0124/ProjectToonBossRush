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
