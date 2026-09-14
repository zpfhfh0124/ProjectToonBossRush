using UnityEngine;

namespace ToonBossRush.Boss
{
    /// <summary>
    /// 보스 패턴 1개를 데이터로 정의. 보스마다 여러 개를 리스트로 들고 랜덤/순차로 선택해서 사용.
    /// 새 보스를 추가할 때 코드 수정 없이 ScriptableObject 에셋만 새로 만들면 됨.
    /// </summary>
    [CreateAssetMenu(menuName = "ToonBossRush/Boss Attack Pattern", fileName = "NewAttackPattern")]
    public class BossAttackPatternSO : ScriptableObject
    {
        [Header("식별")]
        public string patternName = "Attack A";
        [TextArea] public string designNote;

        [Header("타이밍 (초)")]
        [Tooltip("공격 전 예고 동작 시간 — 플레이어가 회피 타이밍을 읽는 구간")]
        public float telegraphDuration = 0.8f;
        public float attackDuration = 0.5f;
        public float recoverDuration = 0.6f;

        [Header("판정")]
        public float damage = 15f;
        [Tooltip("이 패턴을 다시 선택하기 전 최소 대기 시간(연속 남발 방지)")]
        public float minReuseInterval = 3f;

        [Header("애니메이터 연동")]
        [Tooltip("Animator에 SetTrigger로 넘길 파라미터 이름")]
        public string animatorTrigger = "AttackA";
    }
}
