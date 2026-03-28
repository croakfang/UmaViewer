using UnityEngine;

namespace Gallop.Live.Cutt
{
    public struct BlinkLightUpdateInfo
    {
        public LiveTimelineBlinkLightData data;
        public LiveTimelineKeyBlinkLightData key;
        public float localTime;
        public float currentFrame;
        public float currentLiveTime;
    }

    public delegate void BlinkLightUpdateInfoDelegate(ref BlinkLightUpdateInfo updateInfo);
}
