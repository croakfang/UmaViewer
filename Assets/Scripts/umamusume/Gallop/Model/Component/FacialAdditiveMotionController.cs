using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Gallop.Model.Component
{
    public class FacialAdditiveMotionController
    {
        private Transform _parent;
        private Animator _animator;
        private string _name;
        private float _speed = 1f;
        private PlayableGraph _playableGraph;
        private AnimationPlayableOutput _animationPlayableOutput;
        private AnimationLayerMixerPlayable _layerMixerPlayable;
        private AnimationMixerPlayable _additiveMixerPlayableMotion;
        private AnimationScriptPlayable _referenceJob;
        private AnimationClipPlayable clipPlayableLast;
        private Transform[] bones;
        private NativeArray<TransformStreamHandle> _nativeBoneHandles;
        private NativeArray<BoneTransformData> _targetBoneDatas;

        public static implicit operator bool(FacialAdditiveMotionController instance)
        {
            return instance != null;
        }

        public FacialAdditiveMotionController(Transform parent, string name, float speed)
        {
            _parent = parent;
            _name = name;
            _speed = speed;
        }

        public void StartPlay(AnimationClip clip)
        {

            if (_animator == null && _parent != null)
            {
                if (!_parent.TryGetComponent(out _animator))
                {
                    _animator = _parent.gameObject.AddComponent<Animator>();
                }

                _playableGraph = PlayableGraph.Create($"{_name}_FacialGraph");
                _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                _layerMixerPlayable = AnimationLayerMixerPlayable.Create(_playableGraph, 2);
                _layerMixerPlayable.SetLayerAdditive(0, false);
                _layerMixerPlayable.SetLayerAdditive(1, true);

                bones = _animator.GetComponentsInChildren<Transform>();
                _nativeBoneHandles = new NativeArray<TransformStreamHandle>(bones.Length, Allocator.Persistent);
                _targetBoneDatas = new NativeArray<BoneTransformData>(bones.Length, Allocator.Persistent);
                for(int i = 0; i< bones.Length; i++)
                {
                    _nativeBoneHandles[i] = _animator.BindStreamTransform(bones[i]);
                    _targetBoneDatas[i] = new BoneTransformData()
                    {
                        localPosition = bones[i].localPosition,
                        localRotation = bones[i].localRotation,
                        localScale = bones[i].localScale
                    };
                }
                ReferencePoseJob referencePosePlayable = new ReferencePoseJob()
                {
                    boneHandles = _nativeBoneHandles.AsReadOnly(),
                    targetBoneDatas = _targetBoneDatas
                };
                _referenceJob = AnimationScriptPlayable.Create(_playableGraph, referencePosePlayable);
                _referenceJob.SetProcessInputs(false);
                _playableGraph.Connect(_referenceJob, 0, _layerMixerPlayable, 0);

                _additiveMixerPlayableMotion = AnimationMixerPlayable.Create(_playableGraph, 2);
                _playableGraph.Connect(_additiveMixerPlayableMotion, 0, _layerMixerPlayable, 1);
                
                _animationPlayableOutput = AnimationPlayableOutput.Create(_playableGraph, $"{_name}_FacialGraph", _animator);
                _animationPlayableOutput.SetSourcePlayable(_layerMixerPlayable);
            }

            if (clipPlayableLast.IsValid())
            {
                _playableGraph.Disconnect(_additiveMixerPlayableMotion, 1);
                clipPlayableLast.Destroy();
            }

            if (clip == null)
            {
                _animator.enabled = false;
                return;
            }

            _animator.enabled = true;
            clipPlayableLast = AnimationClipPlayable.Create(_playableGraph, clip);
            clipPlayableLast.SetSpeed(_speed);
            _playableGraph.Connect(clipPlayableLast, 0, _additiveMixerPlayableMotion, 0);
            _additiveMixerPlayableMotion.SetInputWeight(0, 1.0f);

            _layerMixerPlayable.SetInputWeight(0, 1.0f);
            _layerMixerPlayable.SetInputWeight(1, 1.0f);
            _playableGraph.Play();
        }

        public void Update(float deltaTime)
        {
            if (_targetBoneDatas.IsCreated)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    var boneData = _targetBoneDatas[i];
                    boneData.localPosition = bone.localPosition;
                    boneData.localRotation = bone.localRotation;
                    boneData.localScale = bone.localScale;
                    _targetBoneDatas[i] = boneData;
                }
            }

            if (_playableGraph.IsValid())
            {
                _playableGraph.Evaluate(deltaTime);
            }
        }

        public bool IsPlaying()
        {
            return clipPlayableLast.IsValid();
        }

        public void SetSpeed(float speed)
        {
            _speed = speed;
            if (clipPlayableLast.IsValid())
            {
                clipPlayableLast.SetSpeed(speed);
            }
        }

        public void SetTime(float normalizedTime)
        {
            if (clipPlayableLast.IsValid())
            {
                clipPlayableLast.SetTime(clipPlayableLast.GetAnimationClip().length * normalizedTime);
            }
        }

        public void Destruct()
        {

            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }

            if (_layerMixerPlayable.IsValid())
            {
                _layerMixerPlayable.Destroy();
            }

            if (_referenceJob.IsValid())
            {
                _referenceJob.Destroy();
            }

            if (_nativeBoneHandles.IsCreated)
            {
                _nativeBoneHandles.Dispose();
            }

            if (_targetBoneDatas.IsCreated)
            {
                _targetBoneDatas.Dispose();
            }

            if (_additiveMixerPlayableMotion.IsValid())
            {
                _additiveMixerPlayableMotion.Destroy();
            }

            _animator = null;
        }
    }

    public struct ReferencePoseJob : IAnimationJob
    {
        public NativeArray<TransformStreamHandle>.ReadOnly boneHandles;
        public NativeArray<BoneTransformData> targetBoneDatas;

        void IAnimationJob.ProcessAnimation(AnimationStream stream)
        {
            for (int i = 0; i < boneHandles.Length; i++)
            {
                var boneHandle = boneHandles[i];
                var targetData = targetBoneDatas[i];
                boneHandle.SetLocalPosition(stream, targetData.localPosition);
                boneHandle.SetLocalRotation(stream, targetData.localRotation);
                boneHandle.SetLocalScale(stream, targetData.localScale);
            }
        }

        void IAnimationJob.ProcessRootMotion(AnimationStream stream)
        {
            
        }
    }

    public struct BoneTransformData
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
}
