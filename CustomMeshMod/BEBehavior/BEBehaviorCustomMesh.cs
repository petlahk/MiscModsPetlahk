﻿using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CustomMeshMod
{
    public class BEBehaviorCustomMesh : BlockEntityBehavior
    {
        ICoreClientAPI capi;
        MeshRenderer myRenderer;

        BlockCustomMesh blockCustomMesh { get => Blockentity.Block as BlockCustomMesh; }
        public BEBehaviorCustomMesh(BlockEntity blockentity) : base(blockentity)
        {
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            capi = api as ICoreClientAPI;

            //if (capi != null) myRenderer = new MeshRenderer(capi, Blockentity.Pos, c, blockCustomMesh.meshRef);
            //capi?.Event.RegisterRenderer(myRenderer, EnumRenderStage.Opaque);
            capi?.ModLoader.GetModSystem<InstancedMeshRenderer>().AddMesh(Blockentity.Pos, blockCustomMesh);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            OnBlockUnloaded();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
        }
    }
}