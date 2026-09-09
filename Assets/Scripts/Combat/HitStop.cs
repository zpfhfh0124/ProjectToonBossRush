using System.Collections;
using UnityEngine;

namespace ToonBossRush.Combat
{
    /// <summary>
    /// 타격감을 위한 짧은 타임스케일 정지(히트스탑).
    /// 씬에 빈 오브젝트 하나에 붙여서 싱글턴으로 사용.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        public static HitStop Instance { get; private set; }

        private Coroutine _routine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void Trigger(float seconds, float timeScale = 0.02f)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(DoHitStop(seconds, timeScale));
        }

        private IEnumerator DoHitStop(float seconds, float timeScale)
        {
            float originalScale = Time.timeScale;
            Time.timeScale = timeScale;
            // Time.deltaTime이 timeScale의 영향을 받으므로 실시간 대기 사용
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = originalScale;
            _routine = null;
        }
    }
}
