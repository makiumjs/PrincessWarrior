shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Procedural stone material: no textures. Surface color/roughness are driven
// by layered value-noise evaluated on world-space position, so every stone
// block looks unique without an imported bump/albedo map.

uniform vec3 base_color : source_color = vec3(0.34, 0.33, 0.31);
uniform vec3 crevice_color : source_color = vec3(0.12, 0.11, 0.11);
uniform float roughness_base : hint_range(0.0, 1.0) = 0.9;
uniform float noise_scale = 2.2;
uniform float bump_strength : hint_range(0.0, 1.0) = 0.35;

float hash13(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Trilinear value noise built from the hash above (no textures anywhere).
float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float n000 = hash13(i + vec3(0.0, 0.0, 0.0));
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));

    float nx00 = mix(n000, n100, u.x);
    float nx10 = mix(n010, n110, u.x);
    float nx01 = mix(n001, n101, u.x);
    float nx11 = mix(n011, n111, u.x);
    float nxy0 = mix(nx00, nx10, u.y);
    float nxy1 = mix(nx01, nx11, u.y);
    return mix(nxy0, nxy1, u.z);
}

float fbm(vec3 p) {
    float sum = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int i = 0; i < 4; i++) {
        sum += value_noise(p * freq) * amp;
        freq *= 2.02;
        amp *= 0.5;
    }
    return sum;
}

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec3 p = world_pos * noise_scale;
    float n = fbm(p);
    float cracks = smoothstep(0.35, 0.55, fbm(p * 2.7 + 11.0));

    vec3 col = mix(crevice_color, base_color, clamp(n * 1.2, 0.0, 1.0));
    col = mix(col, crevice_color, (1.0 - cracks) * 0.4);

    ALBEDO = col;
    ROUGHNESS = clamp(roughness_base + (n - 0.5) * 0.2, 0.0, 1.0);
    METALLIC = 0.0;

    // Fake bump: perturb the shading normal using the noise gradient
    // (finite differences) instead of a normal map texture.
    float e = 0.35;
    float dx = fbm((p + vec3(e, 0.0, 0.0))) - fbm((p - vec3(e, 0.0, 0.0)));
    float dy = fbm((p + vec3(0.0, e, 0.0))) - fbm((p - vec3(0.0, e, 0.0)));
    float dz = fbm((p + vec3(0.0, 0.0, e))) - fbm((p - vec3(0.0, 0.0, e)));
    vec3 grad = vec3(dx, dy, dz);
    NORMAL = normalize(NORMAL - grad * bump_strength);
}
