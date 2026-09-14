using System.Collections;
using UnityEngine;

namespace ToonBossRush.CameraFx
{
    /// <summary>
    /// 타격 시 카메라 흔들림. 메인 카메라(혹은 카메라 리그의 부모)에 붙이고,
    /// Health.OnDamaged 이벤트 등에서 Shake()를 호출.
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        [SerializeField] private float defaultDuration = 0.15f;
        [SerializeField] private float defaultMagnitude = 0.15f;

        private Vector3 _originalLocalPos;
        private Coroutine _routine;

        private void Awake()
        {
            _originalLocalPos = transform.localPosition;
        }

        public void Shake(float duration = -1f, float magnitude = -1f)
        {
            if (duration < 0f) duration = defaultDuration;
            if (magnitude < 0f) magnitude = defaultMagnitude;

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(DoShake(duration, magnitude));
        }

        private IEnumerator DoShake(float duration, float magnitude)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                Vector2 offset = Random.insideUnitCircle * magnitude * (1f - elapsed / duration);
                transform.localPosition = _originalLocalPos + new Vector3(offset.x, offset.y, 0f);
                elapsed += Time.unscaledDeltaTime; // 히트스탑 중에도 흔들리도록 unscaled 사용
                yield return null;
            }
            transform.localPosition = _originalLocalPos;
            _routine = null;
        }
    }
}
