using System.Collections;
using UnityEngine;

namespace ToonBossRush.Combat
{
    /// <summary>
    /// ToonLit 셰이더의 _HitFlashAmount 프로퍼티를 MaterialPropertyBlock으로 조작해
    /// 피격 시 짧게 컬러 플래시를 낸다. Health.OnDamaged에 구독해서 사용.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class ToonHitFlash : MonoBehaviour
    {
        private static readonly int HitFlashAmountId = Shader.PropertyToID("_HitFlashAmount");

        [SerializeField] private float flashDuration = 0.12f;
        [SerializeField] private Health health; // 비워두면 부모에서 자동 탐색

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Coroutine _routine;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            if (health == null) health = GetComponentInParent<Health>();
        }

        private void OnEnable()
        {
            if (health != null) health.OnDamaged += HandleDamaged;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDamaged -= HandleDamaged;
        }

        private void HandleDamaged(float ratio, Vector3 hitPoint)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Flash());
        }

        private IEnumerator Flash()
        {
            float t = 0f;
            while (t < flashDuration)
            {
                t += Time.unscaledDeltaTime;
                float amount = 1f - Mathf.Clamp01(t / flashDuration);
                SetFlash(amount);
                yield return null;
            }
            SetFlash(0f);
        }

        private void SetFlash(float amount)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HitFlashAmountId, amount);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
