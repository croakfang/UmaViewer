using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Gallop
{
    public class FacialAdditiveMotionAsset : ScriptableObject
    {
        [SerializeField]
        private AnimationClip _animationClip;

        public AnimationClip AnimationClip { get=>_animationClip; }

    }

}
