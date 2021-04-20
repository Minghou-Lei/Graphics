half4 _RendererColor;

PackedVaryings vert(Attributes input)
{
    Varyings output = (Varyings)0;
    output = BuildVaryings(input);
    PackedVaryings packedOutput = PackVaryings(output);
    return packedOutput;
}

half4 frag(PackedVaryings packedInput) : SV_TARGET
{
    Varyings unpacked = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(unpacked);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(unpacked);

    SurfaceDescriptionInputs surfaceDescriptionInputs = BuildSurfaceDescriptionInputs(unpacked);
    SurfaceDescription surfaceDescription = SurfaceDescriptionFunction(surfaceDescriptionInputs);

    #if _AlphaClip
        // This isn't defined in the sprite passes. It looks like the built-in legacy shader will use this as it's default constant
        float alphaClipThreshold = 0.01f;
        clip(surfaceDescription.Alpha - alphaClipThreshold);
    #endif

    half4 outColor = 0;
    #ifdef SCENESELECTIONPASS
        // We use depth prepass for scene selection in the editor, this code allow to output the outline correctly
        outColor = half4(_ObjectId, _PassValue, 1.0, 1.0);
    #elif defined(SCENEPICKINGPASS)
        outColor = _SelectionID;
    #endif

    return outColor;
}
