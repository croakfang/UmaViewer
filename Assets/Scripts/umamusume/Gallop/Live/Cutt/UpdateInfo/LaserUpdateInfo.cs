using UnityEngine;

namespace Gallop.Live.Cutt
{
    public struct LaserUpdateInfo
    {
        public LiveTimelineLaserData data;
        public LiveTimelineKeyLaserData key;
        public float localTime;
        public float currentFrame;
        public float currentLiveTime;
    }

    public delegate void LaserUpdateInfoDelegate(ref LaserUpdateInfo updateInfo);
}

