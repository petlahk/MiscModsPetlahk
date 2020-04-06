using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Common;
using System.Collections.Generic;
using Vintagestory.Client.NoObf;

namespace CustomMeshMod
{
    public class InstancedMeshRenderer : ModSystem, IRenderer
    {
        private ICoreClientAPI capi;
        public Matrixf ModelMat = new Matrixf();
        public LoadCustomModels models { get => capi.ModLoader.GetModSystem<LoadCustomModels>(); }
        Dictionary<int, MeshData> meshDataPerID = new Dictionary<int, MeshData>();
        Dictionary<int, MeshRef> meshRefPerID = new Dictionary<int, MeshRef>();
        IShaderProgram prog;
        Dictionary<int, int> RenderedPerID = new Dictionary<int, int>();
        Dictionary<BlockPos, int> IDPerPos = new Dictionary<BlockPos, int>();

        public bool LoadShader()
        {
            prog = capi.Shader.NewShaderProgram();

            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

            capi.Shader.RegisterFileShaderProgram("standardinstanced", prog);

            return prog.Compile();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "CustomMeshes");

            capi.Event.ReloadShader += LoadShader;
            LoadShader();
        }

        public void AddMesh(BlockPos pos, BlockCustomMesh block)
        {
            IDPerPos[pos] = block.Id;
            if (meshRefPerID.ContainsKey(block.Id)) meshRefPerID[block.Id]?.Dispose();

            if (meshDataPerID.ContainsKey(block.Id))
            {
                if (meshDataPerID[block.Id].CustomFloats == null)
                {
                    meshDataPerID[block.Id].CustomFloats = new CustomMeshDataPartFloat((16 + 4) * 10100)
                    {
                        Instanced = true,
                        InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                        InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                        InterleaveStride = 16 + 4 * 16,
                        StaticDraw = false,
                    };

                    meshDataPerID[block.Id].CustomFloats.SetAllocationSize((16 + 4) * 10100);
                }
            }
            else
            {
                meshDataPerID[block.Id] = block.mesh.Clone();

                meshDataPerID[block.Id].CustomFloats = new CustomMeshDataPartFloat((16 + 4) * 10100)
                {
                    Instanced = true,
                    InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                    InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                    InterleaveStride = 16 + 4 * 16,
                    StaticDraw = false,
                };
                meshDataPerID[block.Id].CustomFloats.SetAllocationSize((16 + 4) * 10100);
            }
            if (RenderedPerID.ContainsKey(block.Id)) RenderedPerID[block.Id]++;
            else RenderedPerID[block.Id] = 0;

            Rebuild();
        }

        public void RemoveMesh(BlockPos pos)
        {

        }

        public void Rebuild()
        {
            foreach (var val in IDPerPos)
            {
                BlockPos pos = val.Key;
                MeshData mesh = meshDataPerID[val.Value];

                if (meshRefPerID.ContainsKey(val.Value)) meshRefPerID[val.Value].Dispose();

                Vec3d campos = capi.World.Player.Entity.CameraPos;
                Vec3f tmp = new Vec3f();
                tmp.Set((float)(pos.X - campos.X), (float)(pos.Y - campos.Y), (float)(pos.Z - campos.Z));
                Vec4f lightRbs = capi.World.BlockAccessor.GetLightRGBs(pos);

                UpdateLightAndTransformMatrix(mesh.CustomFloats.Values, RenderedPerID[val.Value], tmp, lightRbs, 0, 0, 0);

                var meshRef = capi.Render.UploadMesh(mesh);
                meshRefPerID[val.Value] = meshRef;
            }
        }

        public double RenderOrder => 0.5;

        public int RenderRange => 24;

        public override void Dispose()
        {
            foreach (var val in meshRefPerID)
            {
                val.Value?.Dispose();
            }
        }


        float[] tmpMat = Mat4f.Create();
        double[] quat = Quaterniond.Create();
        float[] qf = new float[4];
        float[] rotMat = Mat4f.Create();

        void UpdateLightAndTransformMatrix(float[] values, int index, Vec3f distToCamera, Vec4f lightRgba, float rotX, float rotY, float rotZ)
        {
            Mat4f.Identity(tmpMat);

            Mat4f.Translate(tmpMat, tmpMat, distToCamera.X, distToCamera.Y, distToCamera.Z);

            Mat4f.Translate(tmpMat, tmpMat, 0.5f, 0.5f, 0.5f);

            quat[0] = 0;
            quat[1] = 0;
            quat[2] = 0;
            quat[3] = 1;
            Quaterniond.RotateX(quat, quat, rotX);
            Quaterniond.RotateY(quat, quat, rotY);
            Quaterniond.RotateZ(quat, quat, rotZ);

            for (int i = 0; i < quat.Length; i++) qf[i] = (float)quat[i];
            Mat4f.Mul(tmpMat, tmpMat, Mat4f.FromQuat(rotMat, qf));

            Mat4f.Translate(tmpMat, tmpMat, -0.5f, -0.5f, -0.5f);

            values[index * 20] = lightRgba.R;
            values[index * 20 + 1] = lightRgba.G;
            values[index * 20 + 2] = lightRgba.B;
            values[index * 20 + 3] = lightRgba.A;

            for (int i = 0; i < 16; i++)
            {
                values[index * 20 + i + 4] = tmpMat[i];
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            IRenderAPI render = capi.Render;
            IShaderProgram activeShader = render.CurrentActiveShader;
            activeShader?.Stop();
            prog.Use();

            foreach (var val in meshRefPerID)
            {
                if (val.Value == null || val.Value.Disposed) continue;

                BlockCustomMesh blockCustomMesh = capi.World.GetBlock(val.Key) as BlockCustomMesh;
                CustomMesh customMesh = blockCustomMesh.customMesh;
                
                Vec3d cameraPos = capi.World.Player.Entity.CameraPos;
                render.GlToggleBlend(true, EnumBlendMode.Standard);
                if (customMesh.BackFaceCulling) render.GlEnableCullFace();
                if (customMesh.Interpolation != TextureMagFilter.Nearest) GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)customMesh.Interpolation);

                prog.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
                prog.Uniform("rgbaAmbientIn", render.AmbientColor);
                prog.Uniform("rgbaBlockIn", ColorUtil.WhiteArgbVec);
                prog.Uniform("rgbaFogIn", render.FogColor);
                prog.Uniform("extraGlow", 0);
                prog.Uniform("fogMinIn", render.FogMin);
                prog.Uniform("fogDensityIn", render.FogDensity);
                prog.Uniform("dontWarpVertices", 0);
                prog.Uniform("addRenderFlags", 0);
                prog.Uniform("extraZOffset", 0);
                prog.Uniform("overlayOpacity", 0);
                prog.Uniform("extraGodray", 0);

                prog.Uniform("normalShaded", 1);
                prog.BindTexture2D("tex", customMesh.TexPos?.atlasTextureId ?? 0, 0);

                prog.Uniform("shading", (int)customMesh.NormalShading);
                prog.Uniform("baseUVin", new Vec2f(customMesh.TexPos?.x1 ?? 0, customMesh.TexPos?.y1 ?? 0));
                prog.Uniform("nrmUVin", new Vec2f(customMesh.NormalPos?.x1 ?? 0, customMesh.NormalPos?.y1 ?? 0));
                prog.Uniform("pbrUVin", new Vec2f(customMesh.PbrPos?.x1 ?? 0, customMesh.PbrPos?.y1 ?? 0));

                prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                prog.UniformMatrix("modelViewMatrix", capi.Render.CameraMatrixOriginf);

                capi.Render.RenderMeshInstanced(val.Value, RenderedPerID[blockCustomMesh.Id]);
                render.GlDisableCullFace();

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            }

            prog.Stop();
            activeShader?.Use();
        }
    }
}