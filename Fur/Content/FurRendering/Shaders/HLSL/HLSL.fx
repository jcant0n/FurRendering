cbuffer Parameters : register(b0)
{
	float4x4 worldViewProj;
	float MaxHairLengh;
	float numLayers;
	float startShadowValue;
};

Texture2D DiffuseTexture 		: register(t0);
Texture2D FurTexture	 		: register(t1);
SamplerState Sampler1			: register(s0);
SamplerState Sampler2			: register(s1);

struct VS_IN
{
	float3 position		: POSITION0;
	float3 normal		: NORMAL0;
	float2 texCoord		: TEXCOORD0;
	uint iid			: SV_InstanceID;
};

struct PS_IN
{
	float4 position		: SV_POSITION;
	float2 texCoord		: TEXCOORD0;
	float layer			: TEXCOORD1;
	float shadow		: TEXCOORD2;
	
};

PS_IN VS(VS_IN input)
{
	PS_IN output = (PS_IN)0;

	float currentLayer = input.iid / numLayers;
	float3 pos = input.position + (input.normal * currentLayer) * MaxHairLengh;
	output.position = mul(float4(pos, 1.0), worldViewProj);
	output.texCoord = input.texCoord;
	output.layer = currentLayer;
	output.shadow = lerp(startShadowValue, 1.0, currentLayer);

	return output;
}

float4 PS(PS_IN input) : SV_Target
{
	float furData = FurTexture.Sample(Sampler2, input.texCoord).r;
	if (input.layer > 0 && (furData == 0 || furData < input.layer))
		discard;

	float4 color = DiffuseTexture.Sample(Sampler1, input.texCoord);
	//float4 color = float4(1, 1, 0, 1);

	color *= input.shadow;

	return color;
}
