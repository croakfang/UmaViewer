using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyLaserData : LiveTimelineKeyWithInterpolate
    {
        // ✅ 这些字段就是你 StageLaserDriver 里在用的
        public Vector3 objectPosition;
        public Vector3 objectRotate;          // 一般是欧拉角
        public Vector3 objectScale = Vector3.one;

        public int blink;                     // 0/1
        public float blinkPeriod;             // 秒 or 帧换算后的秒（看你 driver 用法）
    }

    [Serializable]
    public class LiveTimelineKeyLaserDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLaserData> { }

    [Serializable]
    public class LiveTimelineLaserData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyLaserDataList keys;
    }
}

