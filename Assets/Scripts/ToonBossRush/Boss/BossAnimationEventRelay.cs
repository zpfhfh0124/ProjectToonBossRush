using UnityEngine;

namespace ToonBossRush.Boss
{
    /// <summary>
    /// 애니메이션 이벤트는 Animator가 붙은 오브젝트(보스 모델 자식)의 컴포넌트로만 전달되므로,
    /// 루트의 BossStateMachine까지 이어주는 중계용 컴포넌트.
    /// 공격 클립의 이벤트 함수 이름(AttackHitboxOn/AttackHitboxOff)은 BossAnimatorBuilder가 임포트 설정에 심는다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class BossAnimationEventRelay : MonoBehaviour
    {
        private BossStateMachine _boss;

        private void Awake()
        {
            _boss = GetComponentInParent<BossStateMachine>();
        }

        public void AttackHitboxOn()
        {
            if (_boss != null) _boss.SetAttackHitboxActive(true);
        }

        public void AttackHitboxOff()
        {
            if (_boss != null) _boss.SetAttackHitboxActive(false);
        }
    }
}
