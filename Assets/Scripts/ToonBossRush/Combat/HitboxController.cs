using System.Collections.Generic;
using UnityEngine;

namespace ToonBossRush.Combat
{
    /// <summary>
    /// 공격 애니메이션의 특정 구간에서만 켜지는 히트박스.
    /// 애니메이션 이벤트로 Activate()/Deactivate()를 호출해서 사용한다.
    /// (Unity 에디터에서 공격 클립에 이벤트 2개를 추가해야 함 — README 참고)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HitboxController : MonoBehaviour
    {
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private float damage = 10f;
        [SerializeField] private bool destroyOwnerHitstop = true;
        [SerializeField] private float hitStopSeconds = 0.05f;

        private Collider _collider;
        private readonly HashSet<Health> _alreadyHit = new HashSet<Health>();

        public float Damage
        {
            get => damage;
            set => damage = value;
        }

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
            _collider.enabled = false;
        }

        /// <summary>애니메이션 이벤트에서 호출 — 히트박스 켜기, 히트 목록 초기화</summary>
        public void Activate()
        {
            _alreadyHit.Clear();
            _collider.enabled = true;
        }

        /// <summary>애니메이션 이벤트에서 호출 — 히트박스 끄기</summary>
        public void Deactivate()
        {
            _collider.enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if ((targetLayers.value & (1 << other.gameObject.layer)) == 0) return;

            var health = other.GetComponentInParent<Health>();
            if (health == null || _alreadyHit.Contains(health)) return;

            _alreadyHit.Add(health);
            health.ApplyDamage(damage, other.ClosestPoint(transform.position));

            if (destroyOwnerHitstop && hitStopSeconds > 0f)
            {
                HitStop.Instance?.Trigger(hitStopSeconds);
            }
        }
    }
}
