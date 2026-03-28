using UnityEngine;
using System.Text;
using UnityEngine.Playables;

public class DumpSwingMotionSource : MonoBehaviour
{
    void Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[DumpSwing] path={GetPath(transform)} activeInHierarchy={gameObject.activeInHierarchy}");

        var anim = GetComponentInParent<Animation>(true);
        var animator = GetComponentInParent<Animator>(true);
        var pd = GetComponentInParent<PlayableDirector>(true);

        sb.AppendLine($"Animation={(anim?"YES":"NO")} Animator={(animator?"YES":"NO")} PlayableDirector={(pd?"YES":"NO")}");

        if (anim)
        {
            sb.AppendLine($"  isPlaying={anim.isPlaying} cullingType={anim.cullingType} defaultClip={(anim.clip?anim.clip.name:"<null>")}");
            int count = 0;
            foreach (AnimationState st in anim)
            {
                count++;
                sb.AppendLine($"  state[{count}] {st.name}: enabled={st.enabled} time={st.time:F3} len={st.length:F3} speed={st.speed:F2} weight={st.weight:F2}");
            }
            if (count == 0) sb.AppendLine("  !!! NO AnimationState (likely missing clips / dependencies not loaded)");
        }

        Debug.Log(sb.ToString(), this);
    }

    static string GetPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        while (t.parent) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }
}
