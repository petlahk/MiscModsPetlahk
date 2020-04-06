#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec2 uvIn;
layout(location = 2) in vec4 colorIn;
layout(location = 3) in int flags;

layout(location = 4) in vec4 rgbaLightIn;		// Per instance
layout(location = 5) in mat4 transform;	 	// Per instance

uniform vec4 rgbaTint;
uniform vec3 rgbaAmbientIn;
uniform vec4 rgbaGlowIn;
uniform vec4 rgbaBlockIn;
uniform vec4 rgbaFogIn;
uniform int extraGlow;
uniform float fogMinIn;
uniform float fogDensityIn;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

uniform int dontWarpVertices;
uniform int addRenderFlags;
uniform float extraZOffset;

out vec2 uv;

uniform vec2 baseUVin;
uniform vec2 nrmUVin;
uniform vec2 pbrUVin;

out vec2 normalUV;
out vec2 pbrUV;

out vec4 color;
out vec4 rgbaFog;
out vec4 rgbaGlow;
out float fogAmount;
out vec3 vertexPosition;

flat out int renderFlags;
out vec3 normal;
flat out vec3 flatNormal;


#include shadowcoords.vsh
#include fogandlight.vsh
#include vertexwarp.vsh

void main(void)
{
	vec4 worldPos = transform * vec4(vertexPositionIn, 1.0);
	
	if (dontWarpVertices == 0) {
		worldPos = applyVertexWarping(flags | addRenderFlags, worldPos);
	}
	vertexPosition = worldPos.xyz;
	
	vec4 camPos = modelViewMatrix * worldPos;
	
	uv = uvIn;
	normalUV = nrmUVin + (uv - baseUVin);
	pbrUV = pbrUVin + (uv - baseUVin);

	int glow = min(255, extraGlow + (flags & 0xff));
	renderFlags = glow | (flags & ~0xff);
	rgbaGlow = rgbaGlowIn;
	
	color = rgbaTint * applyLight(rgbaAmbientIn, rgbaLightIn, colorIn * rgbaBlockIn, renderFlags, camPos);
	color.rgb = mix(color.rgb, rgbaGlowIn.rgb, glow / 255.0 * rgbaGlowIn.a);
		
	rgbaFog = rgbaFogIn;
	gl_Position = projectionMatrix * camPos;
	calcShadowMapCoords(modelViewMatrix, worldPos);
	
	fogAmount = getFogLevel(worldPos, fogMinIn, fogDensityIn);
	
	gl_Position.w += extraZOffset;
	
	normal = unpackNormal(flags >> 15);
	flatNormal = normalize((transform * vec4(normal.x, normal.y, normal.z, 0)).xyz);
}