// Some mobile anisotropic samplers smooth magnified texels. Fetch the exact
// source texel while magnified; retain nearest mipmap/anisotropic minification.
vec4 pixel_sample(sampler2D source, vec2 uv) {
    vec2 dimensions = vec2(textureSize(source, 0));
    float footprint = max(length(dFdx(uv) * dimensions), length(dFdy(uv) * dimensions));
    if (footprint <= 1.0) {
        ivec2 texel = ivec2(clamp(floor(uv * dimensions), vec2(0.0), dimensions - vec2(1.0)));
        return texelFetch(source, texel, 0);
    }
    return texture(source, uv);
}
