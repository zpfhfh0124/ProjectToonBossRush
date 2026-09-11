using System;
using UnityEngine;

namespace ToonBossRush.Combat
{
    /// <summary>
    /// 플레이어/보스 공용 체력 컴포넌트.
    /// 데미지 처리, 스태거(경직), 사망 이벤트를 모두 여기서 관리한다.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float staggerThreshold = 30f; // 이 수치 이상 누적 피격 시 스태거
        [SerializeField] private float staggerDecayPerSecond = 15f;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        private float _staggerAccum;

        /// <param name="amount">받은 데미지</param>
        /// <param name="hitPoint">이펙트/카메라 셰이크 트리거용 피격 지점(월드 좌표)</param>
        public event Action<float, Vector3> OnDamaged; // (currentRatio, hitPoint)
        public event Action OnStaggered;
        public event Action OnDied;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        private void Update()
        {
            if (_staggerAccum > 0f)
            {
                _staggerAccum = Mathf.Max(0f, _staggerAccum - staggerDecayPerSecond * Time.deltaTime);
            }
        }

        public void ApplyDamage(float amount, Vector3 hitPoint)
        {
            if (IsDead || amount <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            _staggerAccum += amount;

            OnDamaged?.Invoke(CurrentHealth / maxHealth, hitPoint);

            if (_staggerAccum >= staggerThreshold)
            {
                _staggerAccum = 0f;
                OnStaggered?.Invoke();
            }

            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                OnDied?.Invoke();
            }
        }

        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            IsDead = false;
            _staggerAccum = 0f;
        }
    }
}
