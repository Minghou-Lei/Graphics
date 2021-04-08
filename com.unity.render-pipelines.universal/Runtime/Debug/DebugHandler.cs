using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace UnityEngine.Rendering.Universal
{
    public class DebugHandler : IDebugDisplaySettingsQuery
    {
        #region Property Id Constants
        static readonly int k_DebugColorInvalidModePropertyId = Shader.PropertyToID("_DebugColorInvalidMode");
        static readonly int k_DebugNumberTexturePropertyId = Shader.PropertyToID("_DebugNumberTexture");

        static readonly int k_DebugColorPropertyId = Shader.PropertyToID("_DebugColor");
        static readonly int k_DebugTexturePropertyId = Shader.PropertyToID("_DebugTexture");
        static readonly int k_DebugTextureDisplayRect = Shader.PropertyToID("_DebugTextureDisplayRect");

        // Material settings...
        static readonly int k_DebugMaterialModeId = Shader.PropertyToID("_DebugMaterialMode");
        static readonly int k_DebugVertexAttributeModeId = Shader.PropertyToID("_DebugVertexAttributeMode");
        static readonly int k_DebugMaterialValidationModeId = Shader.PropertyToID("_DebugMaterialValidationMode");

        // Rendering settings...
        static readonly int k_DebugMipInfoModeId = Shader.PropertyToID("_DebugMipInfoMode");
        static readonly int k_DebugSceneOverrideModeId = Shader.PropertyToID("_DebugSceneOverrideMode");
        static readonly int k_DebugFullScreenModeId = Shader.PropertyToID("_DebugFullScreenMode");
        static readonly int k_DebugValidationModeId = Shader.PropertyToID("_DebugValidationMode");
        static readonly int k_DebugValidateBelowMinThresholdColorPropertyId = Shader.PropertyToID("_DebugValidateBelowMinThresholdColor");
        static readonly int k_DebugValidateAboveMaxThresholdColorPropertyId = Shader.PropertyToID("_DebugValidateAboveMaxThresholdColor");

        // Lighting settings...
        static readonly int k_DebugLightingModeId = Shader.PropertyToID("_DebugLightingMode");
        static readonly int k_DebugLightingFeatureFlagsId = Shader.PropertyToID("_DebugLightingFeatureFlags");

        static readonly int k_DebugValidateAlbedoMinLuminanceId = Shader.PropertyToID("_DebugValidateAlbedoMinLuminance");
        static readonly int k_DebugValidateAlbedoMaxLuminanceId = Shader.PropertyToID("_DebugValidateAlbedoMaxLuminance");
        static readonly int k_DebugValidateAlbedoSaturationToleranceId = Shader.PropertyToID("_DebugValidateAlbedoSaturationTolerance");
        static readonly int k_DebugValidateAlbedoHueToleranceId = Shader.PropertyToID("_DebugValidateAlbedoHueTolerance");
        static readonly int k_DebugValidateAlbedoCompareColorId = Shader.PropertyToID("_DebugValidateAlbedoCompareColor");

        static readonly int k_DebugValidateMetallicMinValueId = Shader.PropertyToID("_DebugValidateMetallicMinValue");
        static readonly int k_DebugValidateMetallicMaxValueId = Shader.PropertyToID("_DebugValidateMetallicMaxValue");

        static readonly int k_ValidationChannelsId = Shader.PropertyToID("_ValidationChannels");
        static readonly int k_RangeMinimumId = Shader.PropertyToID("_RangeMinimum");
        static readonly int k_RangeMaximumId = Shader.PropertyToID("_RangeMaximum");
        #endregion

        readonly Texture2D m_NumberFontTexture;
        readonly Material m_ReplacementMaterial;

        bool m_HasDebugRenderTarget;
        Vector4 m_DebugRenderTargetPixelRect;
        RenderTargetIdentifier m_DebugRenderTargetIdentifier;

        readonly DebugDisplaySettings m_DebugDisplaySettings;

        DebugDisplaySettingsLighting LightingSettings => m_DebugDisplaySettings.LightingSettings;
        DebugDisplaySettingsMaterial MaterialSettings => m_DebugDisplaySettings.MaterialSettings;
        DebugDisplaySettingsRendering RenderingSettings => m_DebugDisplaySettings.RenderingSettings;

        #region IDebugDisplaySettingsQuery
        public bool AreAnySettingsActive => m_DebugDisplaySettings.AreAnySettingsActive;
        public bool IsPostProcessingAllowed => m_DebugDisplaySettings.IsPostProcessingAllowed;
        public bool IsLightingActive => m_DebugDisplaySettings.IsLightingActive;

        // These modes would require putting custom data into gbuffer, so instead we just disable deferred mode.
        /// <summary>
        /// Returns true if forward rendering is used to render this debug mode, even if deferred rendering is active.
        /// </summary>
        public bool IsActiveModeUnsupportedForDeferred =>
            m_DebugDisplaySettings.RenderingSettings.debugSceneOverrideMode == DebugSceneOverrideMode.Overdraw ||
            m_DebugDisplaySettings.MaterialSettings.DebugVertexAttributeIndexData != DebugVertexAttributeMode.None;

        public bool TryGetScreenClearColor(ref Color color)
        {
            return m_DebugDisplaySettings.TryGetScreenClearColor(ref color);
        }

        #endregion

        internal Material ReplacementMaterial => m_ReplacementMaterial;
        internal DebugDisplaySettings DebugDisplaySettings => m_DebugDisplaySettings;

        /// <summary>
        /// Returns true if the current debug mode requires screen to be cleared.
        /// </summary>
        public bool IsScreenClearNeeded
        {
            get
            {
                Color color = Color.black;

                return TryGetScreenClearColor(ref color);
            }
        }

        public DebugHandler(ScriptableRendererData scriptableRendererData)
        {
            Texture2D numberFontTexture = scriptableRendererData.debugShaders.NumberFont;
            Shader debugReplacementShader = scriptableRendererData.debugShaders.debugReplacementPS;

            m_DebugDisplaySettings = DebugDisplaySettings.Instance;

            m_NumberFontTexture = numberFontTexture;
            m_ReplacementMaterial = (debugReplacementShader == null) ? null : CoreUtils.CreateEngineMaterial(debugReplacementShader);
        }

        /// <summary>
        /// Returns true if any debug mode is active for the provided camera.
        /// </summary>
        /// <param name="cameraData"></param>
        /// <returns></returns>
        public bool IsActiveForCamera(ref CameraData cameraData)
        {
            return !cameraData.isPreviewCamera && AreAnySettingsActive;
        }

        /// <summary>
        /// Returns information about a possibly active on-screen debug mode.
        /// </summary>
        /// <param name="debugFullScreenMode">Currently active on-screen debug mode.</param>
        /// <param name="outputHeight">The expected pixel height of the debug view on screen.</param>
        /// <returns>True if an on-screen debug mode is active.</returns>
        public bool TryGetFullscreenDebugMode(out DebugFullScreenMode debugFullScreenMode, out int outputHeight)
        {
            debugFullScreenMode = RenderingSettings.debugFullScreenMode;
            outputHeight = RenderingSettings.debugFullScreenModeOutputSize;
            return debugFullScreenMode != DebugFullScreenMode.None;
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        internal void SetupShaderProperties(CommandBuffer cmd, int passIndex = 0)
        {
            if (LightingSettings.DebugLightingMode == DebugLightingMode.ShadowCascades)
            {
                // we disable cubemap reflections, too distracting (in TemplateLWRP for ex.)
                cmd.EnableShaderKeyword("_DEBUG_ENVIRONMENTREFLECTIONS_OFF");
            }
            else
            {
                cmd.DisableShaderKeyword("_DEBUG_ENVIRONMENTREFLECTIONS_OFF");
            }

            switch (RenderingSettings.debugSceneOverrideMode)
            {
                case DebugSceneOverrideMode.Overdraw:
                {
                    cmd.SetGlobalColor(k_DebugColorPropertyId, new Color(0.1f, 0.01f, 0.01f, 1));
                    break;
                }

                case DebugSceneOverrideMode.Wireframe:
                {
                    cmd.SetGlobalColor(k_DebugColorPropertyId, Color.black);
                    break;
                }

                case DebugSceneOverrideMode.SolidWireframe:
                {
                    cmd.SetGlobalColor(k_DebugColorPropertyId, (passIndex == 0) ? Color.white : Color.black);
                    break;
                }

                case DebugSceneOverrideMode.ShadedWireframe:
                {
                    if (passIndex == 1)
                    {
                        cmd.SetGlobalColor(k_DebugColorPropertyId, Color.black);
                    }
                    break;
                }
            }

            switch (MaterialSettings.MaterialValidationMode)
            {
                case DebugMaterialValidationMode.Albedo:
                    cmd.SetGlobalFloat(k_DebugValidateAlbedoMinLuminanceId, MaterialSettings.AlbedoMinLuminance);
                    cmd.SetGlobalFloat(k_DebugValidateAlbedoMaxLuminanceId, MaterialSettings.AlbedoMaxLuminance);
                    cmd.SetGlobalFloat(k_DebugValidateAlbedoSaturationToleranceId, MaterialSettings.AlbedoSaturationTolerance);
                    cmd.SetGlobalFloat(k_DebugValidateAlbedoHueToleranceId, MaterialSettings.AlbedoHueTolerance);
                    cmd.SetGlobalColor(k_DebugValidateAlbedoCompareColorId, MaterialSettings.AlbedoCompareColor.linear);
                    break;

                case DebugMaterialValidationMode.Metallic:
                    cmd.SetGlobalFloat(k_DebugValidateMetallicMinValueId, MaterialSettings.MetallicMinValue);
                    cmd.SetGlobalFloat(k_DebugValidateMetallicMaxValueId, MaterialSettings.MetallicMaxValue);
                    break;
            }
        }

        /// <summary>
        /// Sets the provided render target to be displayed by the Rendering Debugger.
        /// </summary>
        /// <param name="renderTargetIdentifier">Render target identifier.</param>
        /// <param name="displayRect">Pixel dimensions of the rectangle used to render the debug texture.</param>
        public void SetDebugRenderTarget(RenderTargetIdentifier renderTargetIdentifier, Rect displayRect)
        {
            m_HasDebugRenderTarget = true;
            m_DebugRenderTargetIdentifier = renderTargetIdentifier;
            m_DebugRenderTargetPixelRect = new Vector4(displayRect.x, displayRect.y, displayRect.width, displayRect.height);
        }

        /// <summary>
        /// Clear currently active debug render target.
        /// </summary>
        public void ResetDebugRenderTarget()
        {
            m_HasDebugRenderTarget = false;
        }

        /// <summary>
        /// Sets the shader properties and keywords for the final debug pass.
        /// </summary>
        /// <param name="cmd">Associated command buffer.</param>
        /// <param name="cameraData">Camera being rendered.</param>
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public void UpdateShaderGlobalPropertiesFinalBlitPass(CommandBuffer cmd, ref CameraData cameraData)
        {
            if (IsActiveForCamera(ref cameraData))
            {
                cmd.EnableShaderKeyword("_DEBUG_SHADER");
            }
            else
            {
                cmd.DisableShaderKeyword("_DEBUG_SHADER");
            }

            if (m_HasDebugRenderTarget)
            {
                cmd.SetGlobalTexture(k_DebugTexturePropertyId, m_DebugRenderTargetIdentifier);
                cmd.SetGlobalVector(k_DebugTextureDisplayRect, m_DebugRenderTargetPixelRect);
            }

            var renderingSettings = m_DebugDisplaySettings.RenderingSettings;
            if (renderingSettings.validationMode == DebugValidationMode.HighlightOutsideOfRange)
            {
                cmd.SetGlobalInteger(k_ValidationChannelsId, (int)renderingSettings.validationChannels);
                cmd.SetGlobalFloat(k_RangeMinimumId, renderingSettings.ValidationRangeMin);
                cmd.SetGlobalFloat(k_RangeMaximumId, renderingSettings.ValidationRangeMax);
            }
        }

        /// <summary>
        /// Sets the majority of shader properties and keywords.
        /// </summary>
        /// <param name="context"></param>
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public void Setup(ScriptableRenderContext context)
        {
            var cmd = CommandBufferPool.Get("");

            cmd.DisableShaderKeyword("_DEBUG_SHADER");

            // Material settings...
            cmd.SetGlobalFloat(k_DebugMaterialModeId, (int)MaterialSettings.DebugMaterialModeData);
            cmd.SetGlobalFloat(k_DebugVertexAttributeModeId, (int)MaterialSettings.DebugVertexAttributeIndexData);

            cmd.SetGlobalInteger(k_DebugMaterialValidationModeId, (int)MaterialSettings.MaterialValidationMode);

            // Rendering settings...
            cmd.SetGlobalInteger(k_DebugMipInfoModeId, (int)RenderingSettings.debugMipInfoMode);
            cmd.SetGlobalInteger(k_DebugSceneOverrideModeId, (int)RenderingSettings.debugSceneOverrideMode);
            cmd.SetGlobalInteger(k_DebugFullScreenModeId, (int)RenderingSettings.debugFullScreenMode);
            cmd.SetGlobalInteger(k_DebugValidationModeId, (int)RenderingSettings.validationMode);
            cmd.SetGlobalColor(k_DebugValidateBelowMinThresholdColorPropertyId, Color.red);
            cmd.SetGlobalColor(k_DebugValidateAboveMaxThresholdColorPropertyId, Color.blue);

            // Lighting settings...
            cmd.SetGlobalFloat(k_DebugLightingModeId, (int)LightingSettings.DebugLightingMode);
            cmd.SetGlobalInteger(k_DebugLightingFeatureFlagsId, (int)LightingSettings.DebugLightingFeatureFlagsMask);

            // Set-up any other persistent properties...
            cmd.SetGlobalColor(k_DebugColorInvalidModePropertyId, Color.red);
            cmd.SetGlobalTexture(k_DebugNumberTexturePropertyId, m_NumberFontTexture);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        #region DebugRenderPasses
        private class DebugRenderPassEnumerable : IEnumerable<DebugRenderSetup>
        {
            private class Enumerator : IEnumerator<DebugRenderSetup>
            {
                private readonly DebugHandler m_DebugHandler;
                private readonly ScriptableRenderContext m_Context;
                private readonly CommandBuffer m_CommandBuffer;
                private readonly int m_NumIterations;

                private int m_Index;

                public DebugRenderSetup Current { get; private set; }
                object IEnumerator.Current => Current;

                public Enumerator(DebugHandler debugHandler, ScriptableRenderContext context, CommandBuffer commandBuffer)
                {
                    DebugSceneOverrideMode sceneOverrideMode = debugHandler.DebugDisplaySettings.RenderingSettings.debugSceneOverrideMode;

                    m_DebugHandler = debugHandler;
                    m_Context = context;
                    m_CommandBuffer = commandBuffer;
                    m_NumIterations = ((sceneOverrideMode == DebugSceneOverrideMode.SolidWireframe) || (sceneOverrideMode == DebugSceneOverrideMode.ShadedWireframe)) ? 2 : 1;

                    m_Index = -1;
                }

                #region IEnumerator<DebugRenderSetup>
                public bool MoveNext()
                {
                    Current?.Dispose();

                    if (++m_Index >= m_NumIterations)
                    {
                        return false;
                    }
                    else
                    {
                        Current = new DebugRenderSetup(m_DebugHandler, m_Context, m_CommandBuffer, m_Index);
                        return true;
                    }
                }

                public void Reset()
                {
                    if (Current != null)
                    {
                        Current.Dispose();
                        Current = null;
                    }
                    m_Index = -1;
                }

                public void Dispose()
                {
                    Current?.Dispose();
                }

                #endregion
            }

            private readonly DebugHandler m_DebugHandler;
            private readonly ScriptableRenderContext m_Context;
            private readonly CommandBuffer m_CommandBuffer;

            public DebugRenderPassEnumerable(DebugHandler debugHandler, ScriptableRenderContext context, CommandBuffer commandBuffer)
            {
                m_DebugHandler = debugHandler;
                m_Context = context;
                m_CommandBuffer = commandBuffer;
            }

            #region IEnumerable<DebugRenderSetup>
            public IEnumerator<DebugRenderSetup> GetEnumerator()
            {
                return new Enumerator(m_DebugHandler, m_Context, m_CommandBuffer);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            #endregion
        }

        public IEnumerable<DebugRenderSetup> CreateDebugRenderSetupEnumerable(ScriptableRenderContext context,
            CommandBuffer commandBuffer)
        {
            return new DebugRenderPassEnumerable(this, context, commandBuffer);
        }

        #endregion
    }
}
