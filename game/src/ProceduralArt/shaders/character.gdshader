shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Procedural "cloth/skin" material for the character silhouette. No texture
// sampling: a coarse hash-noise weave pattern breaks up the flat capsule
// shading, plus a rim-light term so the segmented body reads clearly as a
// character silhouette against the environment blocks even at a distance.

uniform vec3 base_color : source_color = vec3(0.75, 0.30, 0.22);
uniform vec3 weave_color : source_color = vec3(0.55, 0.20, 0.14);
uniform float weave_scale = 40.0;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.6;
uniform vec3 rim_color : source_color = vec3(1.0, 0.92, 0.78);
uniform float rim_strength : hint_range(0.0, 2.0) = 0.6;

float hash13(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.05, 0.15, 0.25));
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
varying vec3 view_normal;

void vertex() {
    obj_pos = VERTEX;
    view_normal = NORMAL;
}

void fragment() {
    float weave = value_noise(obj_pos * weave_scale);
    vec3 col = mix(weave_color, base_color, smoothstep(0.35, 0.65, weave));

    // Cheap rim light using the view-space normal vs. view direction.
    float fres = pow(1.0 - clamp(dot(normalize(view_normal), vec3(0.0, 0.0, 1.0)), 0.0, 1.0), 3.0);

    ALBEDO = col;
    EMISSION = rim_color * fres * rim_strength;
    ROUGHNESS = roughness_base;
    METALLIC = 0.0;
}
