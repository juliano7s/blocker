shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float intensity : hint_range(0.0, 1.0) = 0.4;
uniform float softness : hint_range(0.0, 1.0) = 0.5;
uniform float radius : hint_range(0.0, 2.0) = 0.8;
uniform vec3 vignette_color : source_color = vec3(0.02, 0.02, 0.06);

void fragment() {
    vec4 scene = texture(screen_texture, SCREEN_UV);
    vec2 uv = SCREEN_UV * 2.0 - 1.0;
    float dist = length(uv);
    float vignette = smoothstep(radius, radius - softness, dist);
    vec3 color = mix(vignette_color, scene.rgb, mix(1.0, vignette, intensity));
    COLOR = vec4(color, 1.0);
}
