shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Procedural wood material: concentric-ring "grain" pattern generated purely
// from noise math on object-space position, no textures. Rings run around
// the beam's long axis (object-space X) the way a sawn plank would show them.

uniform vec3 light_wood : source_color = vec3(0.62, 0.44, 0.26);
uniform vec3 dark_wood : source_color = vec3(0.33, 0.20, 0.11);
uniform float ring_frequency = 18.0;
uniform float grain_warp = 1.6;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.55;

float hash13(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.11, 0.17, 0.23));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    float n00 = mix(hash13(i + vec3(0.0, 0.0, 0.0)), hash13(i + vec3(1.0, 0.0, 0.0)), u.x);
    float n10 = mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), u.x);
    float n01 = mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), u.x);
    float n11 = mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), u.x);
    float nx0 = mix(n00, n10, u.y);
    float nx1 = mix(n01, n11, u.y);
    return mix(nx0, nx1, u.z);
}

varying vec3 obj_pos;

void vertex() {
    obj_pos = VERTEX;
}

void fragment() {
    // Distance from the beam's long (X) axis, warped by low-frequency noise
    // so rings aren't perfectly circular -> reads as wood, not a barcode.
    float warp = value_noise(obj_pos * 3.0) * grain_warp;
    float radial = length(obj_pos.yz) + warp * 0.1;

    float rings = sin(radial * ring_frequency);
    float ring_mask = smoothstep(-0.2, 0.6, rings);

    // Fine longitudinal grain streaks along X.
    float streak = value_noise(vec3(obj_pos.x * 40.0, obj_pos.y * 6.0, obj_pos.z * 6.0));
    ring_mask = clamp(ring_mask - streak * 0.15, 0.0, 1.0);

    vec3 col = mix(dark_wood, light_wood, ring_mask);

    ALBEDO = col;
    ROUGHNESS = clamp(roughness_base + (1.0 - ring_mask) * 0.25, 0.0, 1.0);
    METALLIC = 0.0;
    SPECULAR = 0.3;
}
