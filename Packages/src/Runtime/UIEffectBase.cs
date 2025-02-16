using System.Runtime.CompilerServices;
using Coffee.UIEffectInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

[assembly: InternalsVisibleTo("UIEffect")]
[assembly: InternalsVisibleTo("Coffee.UIEffect.Editor")]

namespace Coffee.UIEffects
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public abstract class UIEffectBase : UIBehaviour, IMeshModifier, IMaterialModifier, ICanvasRaycastFilter
    {
        private static readonly InternalObjectPool<UIEffectContext> s_ContextPool =
            new InternalObjectPool<UIEffectContext>(() => new UIEffectContext(), x => true, x => x.Reset());

        private Graphic _graphic;
        private Material _material;
        private UIEffectContext _context;

        public Graphic graphic => _graphic ? _graphic : _graphic = GetComponent<Graphic>();
        public virtual uint effectId => (uint)GetInstanceID();
        public virtual float actualSamplingScale => 1;
        public virtual bool canModifyShape => true;

        public virtual UIEffectContext context
        {
            get
            {
                if (_context == null)
                {
                    _context = s_ContextPool.Rent();
                    UpdateContext(_context);
                }

                return _context;
            }
        }

        public virtual RectTransform transitionRoot => transform as RectTransform;

        protected override void OnEnable()
        {
            UpdateContext(context);
            SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            MaterialRepository.Release(ref _material);
            SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnDestroy()
        {
            s_ContextPool.Return(ref _context);
        }

        public void ModifyMesh(Mesh mesh)
        {
        }

        public virtual void ModifyMesh(VertexHelper vh)
        {
            if (!isActiveAndEnabled || context == null) return;

            context.ModifyMesh(graphic, transitionRoot, vh, canModifyShape);
        }

        public virtual Material GetModifiedMaterial(Material baseMaterial)
        {
            if (baseMaterial == null || !isActiveAndEnabled || context == null || !context.willModifyMaterial)
            {
                MaterialRepository.Release(ref _material);
                return baseMaterial;
            }

            Profiler.BeginSample("(UIE)[UIEffect] GetModifiedMaterial");
            var samplingScaleId = (uint)(Mathf.InverseLerp(0.01f, 100, actualSamplingScale) * uint.MaxValue);
            var hash = new Hash128((uint)baseMaterial.GetInstanceID(), effectId, samplingScaleId, 0);
            if (!MaterialRepository.Valid(hash, _material))
            {
                Profiler.BeginSample("(UIE)[UIEffect] GetModifiedMaterial > Get or create material");
                MaterialRepository.Get(hash, ref _material, x =>
                {
                    // Check if this is a TextMeshPro material
                    bool isTMP = x.shader.name.StartsWith("TextMeshPro/") || graphic is TMP_Text;
                    var newMaterial = new Material(x)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    if (isTMP)
                    {
                        // For TextMeshPro, keep the original shader and use material property variants
                        newMaterial.shader = x.shader;
                        newMaterial.CopyPropertiesFromMaterial(x);
                    }
                    else
                    {
                        // For regular UI, use the UIEffect shader variant
                        newMaterial.shader = UIEffectProjectSettings.shaderRegistry.FindOptionalShader(x.shader,
                            "(UIEffect)",
                            "Hidden/{0} (UIEffect)",
                            "Hidden/UI/Default (UIEffect)");
                    }
                    
                    return newMaterial;
                }, baseMaterial);
                Profiler.EndSample();
            }

            ApplyContextToMaterial(_material);
            Profiler.EndSample();
            return _material;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            UpdateContext(context);
            ApplyContextToMaterial(graphic ? graphic.canvasRenderer.GetMaterial() : _material);
            SetVerticesDirty();
            SetMaterialDirty();

            if (!EditorApplication.isPlaying)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        protected override void Reset()
        {
            OnValidate();
        }
#endif

        protected override void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled) return;
            SetVerticesDirty();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            UpdateContext(context);
            SetVerticesDirty();
            SetMaterialDirty();
        }

        public virtual void SetVerticesDirty()
        {
            if (graphic)
            {
                graphic.SetVerticesDirty();
                GraphicProxy.Find(graphic).SetVerticesDirty(graphic, false);
#if UNITY_EDITOR
                EditorApplication.QueuePlayerLoopUpdate();
#endif
            }
        }

        public virtual void SetMaterialDirty()
        {
            if (graphic)
            {
                graphic.SetMaterialDirty();
#if UNITY_EDITOR
                EditorApplication.QueuePlayerLoopUpdate();
#endif
            }
        }

        protected abstract void UpdateContext(UIEffectContext c);

        public virtual void ApplyContextToMaterial(Material material)
        {
            if (!isActiveAndEnabled || context == null || !material) return;

            context.ApplyToMaterial(material, actualSamplingScale);

#if UNITY_EDITOR
            UIEffectProjectSettings.shaderRegistry.RegisterVariant(material, "UI > UIEffect");
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }

        public abstract void SetRate(float rate, UIEffectTweener.CullingMask cullingMask);
        public abstract bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera);
    }
}
