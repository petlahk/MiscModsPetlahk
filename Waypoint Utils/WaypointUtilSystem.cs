﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Newtonsoft.Json;
using System.IO;
using Cairo;
using Vintagestory.API.Util;
using Path = System.IO.Path;
using System.Globalization;

namespace VSHUD
{
    class WaypointUtilSystem : ModSystem
    {
        ICoreClientAPI capi;
        public ConfigLoader cL;
        public WaypointUtilConfig Config;
        public WaypointMapLayer WPLayer { get => capi.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single(); }

        public Dictionary<string, LoadedTexture> texturesByIcon;
        string[] iconKeys { get => texturesByIcon.Keys.ToArray(); }

        public void PopulateTextures()
        {
            if (this.texturesByIcon == null | false)
            {
                this.texturesByIcon = new Dictionary<string, LoadedTexture>();
                ImageSurface surface = new ImageSurface(0, 64, 64);
                Context cr = new Context(surface);
                string[] strArray = new string[13]
                { "circle", "bee", "cave", "home", "ladder", "pick", "rocks", "ruins", "spiral", "star1", "star2", "trader", "vessel" };
                foreach (string text in strArray)
                {
                    cr.Operator = (Operator)0;
                    cr.SetSourceRGBA(0.0, 0.0, 0.0, 0.0);
                    cr.Paint();
                    cr.Operator = (Operator)2;
                    capi.Gui.Icons.DrawIcon(cr, "wp" + text.UcFirst(), 1.0, 1.0, 32.0, 32.0, ColorUtil.WhiteArgbDouble);
                    texturesByIcon[text] = new LoadedTexture(capi, capi.Gui.LoadCairoTexture(surface, true), 20, 20);
                }
                cr.Dispose();
                surface.Dispose();
            }
        }

        public List<Waypoint> Waypoints { get => WPLayer.ownWaypoints; }
        public WaypointRelative[] WaypointsRelSorted { get => WaypointsRel.OrderBy(wp => wp.DistanceFromPlayer).ToArray(); }
        public WaypointRelative[] WaypointsRel {
            get
            {
                Queue<WaypointRelative> rel = new Queue<WaypointRelative>();
                int i = 0;
                foreach (var val in Waypoints)
                {
                    WaypointRelative relative = new WaypointRelative(capi)
                    {
                        Color = val.Color,
                        OwningPlayerGroupId = val.OwningPlayerGroupId,
                        OwningPlayerUid = val.OwningPlayerUid,
                        Position = val.Position,
                        Text = val.Text,
                        Title = val.Title,
                        Icon = val.Icon,
                        Index = i
                    };
                    rel.Enqueue(relative);
                    i++;
                }
                return rel.ToArray();
            }
        }

        public List<HudElementWaypoint> WaypointElements { get; set; } = new List<HudElementWaypoint>();

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;
            PopulateTextures();
            HudElementWaypoint.quadModel = capi.Render.UploadMesh(QuadMeshUtil.GetQuad());

            cL = capi.ModLoader.GetModSystem<ConfigLoader>();
            Config = cL.Config;

            GuiDialogWaypointFrontEnd frontEnd = new GuiDialogWaypointFrontEnd(capi);

            capi.Input.RegisterHotKey("viewwaypoints", "View Waypoints", GlKeys.U, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("viewwaypoints", ViewWaypoints);
            capi.Input.RegisterHotKey("culldeathwaypoints", "Cull Death Waypoints", GlKeys.O, HotkeyType.GUIOrOtherControls);
            capi.Input.RegisterHotKey("waypointfrontend", "Open WaypointUtils GUI", GlKeys.P, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("waypointfrontend", a => { api.Event.RegisterCallback(d => frontEnd.Toggle(), 100); return true; });

            capi.RegisterCommand("wpcfg", "Waypoint Configurtion", "[dotrange|titlerange|perblockwaypoints|purge|waypointprefix|waypointid|enableall]", new ClientChatCommandDelegate(CmdWaypointConfig));

            capi.Event.LevelFinalize += () =>
            {
                EntityPlayer player = api.World.Player.Entity;
                frontEnd.OnOwnPlayerDataReceived();

                player.WatchedAttributes.RegisterModifiedListener("entityDead", () =>
                {
                    if (player?.WatchedAttributes?["entityDead"] == null) return;
                    Vec3d playerPos = player.Pos.XYZ;

                    if (player.WatchedAttributes.GetInt("entityDead") == 1)
                    {
                        string str = string.Format(CultureInfo.InvariantCulture, "/waypoint addati {0} ={1} ={2} ={3} {4} #{5} {6}", "rocks", playerPos.X, playerPos.Y, playerPos.Z, true, ColorStuff.RandomHexColorVClamp(capi, 0.5, 0.8), "Player Death Waypoint");

                        capi.SendChatMessage(str);
                        if (Config.DebugDeathWaypoints) capi.ShowChatMessage("DEBUG: Sent Command: " + str);
                    }
                });

                RepopulateDialogs();
                capi.Event.RegisterCallback(dt => Update(), 100);
            };
            updateID = capi.Event.RegisterGameTickListener(dt => Update(), 30);

            capi.Input.RegisterHotKey("editfloatywaypoint", "Edit Floaty Waypoint", GlKeys.R, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("editfloatywaypoint", (k) =>
            {
                foreach (var val in WaypointElements)
                {
                    if (val.isAligned)
                    {
                        val.OpenEditDialog();
                        return true;
                    }
                }
                return false;
            });
        }
        long updateID;

        private void CmdWaypointConfig(int groupId, CmdArgs args)
        {
            string arg = args.PopWord();
            switch (arg)
            {
                case "deathdebug":
                    Config.DebugDeathWaypoints = args.PopBool() ?? !Config.DebugDeathWaypoints;
                    capi.ShowChatMessage(string.Format("Death waypoint debbuging set to {0}", Config.DebugDeathWaypoints));
                    break;
                case "dotrange":
                    double? dr = args.PopDouble();
                    Config.DotRange = dr != null ? (double)dr : Config.DotRange;
                    capi.ShowChatMessage("Dot Range Set To " + Config.DotRange + " Meters.");
                    break;
                case "titlerange":
                    double? tr = args.PopDouble();
                    Config.TitleRange = tr != null ? (double)tr : Config.TitleRange;
                    capi.ShowChatMessage("Title Range Set To " + Config.TitleRange + " Meters.");
                    break;
                case "perblockwaypoints":
                    bool? pb = args.PopBool();
                    Config.PerBlockWaypoints = pb != null ? (bool)pb : !Config.PerBlockWaypoints;
                    capi.ShowChatMessage("Per Block Waypoints Set To " + Config.PerBlockWaypoints + ".");
                    break;
                case "pdw":
                    PurgeWaypointsByStrings("Player Death Waypoint", "*Player Death Waypoint*");
                    break;
                case "plt":
                    PurgeWaypointsByStrings("Limits Test");
                    break;
                case "open":
                    ViewWaypoints(new KeyCombination());
                    break;
                case "waypointprefix":
                    bool? wp = args.PopBool();
                    Config.WaypointPrefix = wp != null ? (bool)wp : !Config.WaypointPrefix;
                    capi.ShowChatMessage("Waypoint Prefix Set To " + Config.WaypointPrefix + ".");
                    break;
                case "waypointid":
                    bool? wi = args.PopBool();
                    Config.WaypointID = wi != null ? (bool)wi : !Config.WaypointID;
                    capi.ShowChatMessage("Waypoint ID Set To " + Config.WaypointID + ".");
                    break;
                case "purge":
                    string s = args.PopWord();
                    if (s == "reallyreallyconfirm") Purge();
                    else capi.ShowChatMessage(Lang.Get("Are you sure you want to do that? It will remove ALL your waypoints, type \"reallyreallyconfirm\" to confirm."));
                    break;
                case "enableall":
                    Config.DisabledColors.Clear();
                    break;
                case "save":
                    break;
                case "export":
                    string path = (args.PopWord() ?? "waypoints") + ".json";
                    using (TextWriter tw = new StreamWriter(path))
                    {
                        tw.Write(JsonConvert.SerializeObject(WaypointsRel, Formatting.Indented));
                        tw.Close();
                    }
                    break;
                case "import":
                    string path1 = (args.PopWord() ?? "waypoints") + ".json";
                    if (File.Exists(path1))
                    {
                        using (TextReader reader = new StreamReader(path1))
                        {
                            bulkProcessing = true;
                            DummyWaypoint[] relative = JsonConvert.DeserializeObject<DummyWaypoint[]>(reader.ReadToEnd(), new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore});
                            int j = relative.Length;
                            bulkActionID = capi.Event.RegisterGameTickListener(dt => 
                            {
                                j--;
                                if (j < 0)
                                {
                                    capi.Event.UnregisterGameTickListener(bulkActionID);
                                    firstOpen = true;
                                    bulkProcessing = false;
                                    bulkActionID = 0;
                                    return;
                                }

                                var val = relative[j];
                                if (WaypointsRel.Any(w =>
                                val.Position.AsBlockPos.X == w.Position.AsBlockPos.X &&
                                val.Position.AsBlockPos.Y == w.Position.AsBlockPos.Y &&
                                val.Position.AsBlockPos.Z == w.Position.AsBlockPos.Z
                                )) return;

                                string str = string.Format(CultureInfo.InvariantCulture, "/waypoint addati {0} ={1} ={2} ={3} {4} #{5} {6}", val.Icon, val.Position.X, val.Position.Y, val.Position.Z, val.Pinned, ColorUtil.Int2Hex(val.Color), val.Title);
                                capi.SendChatMessage(str);
                            }, 1);
                            reader.Close();
                        }
                    }
                    break;
                case "testlimits":
                    bulkProcessing = true;
                    int amount = args.PopInt() ?? 30;
                    int maxY = args.PopInt() ?? 10;
                    double radius = (args.PopInt() ?? 1000);

                    int i = amount;

                    bulkActionID = capi.Event.RegisterGameTickListener(dt =>
                    {
                        double
                            x = (capi.World.Rand.NextDouble() - 0.5) * radius,
                            y = (capi.World.Rand.NextDouble() - 0.5) * maxY,
                            z = (capi.World.Rand.NextDouble() - 0.5) * radius;

                        double r = x * x + z * z;
                        if (r <= (radius * 0.5) * (radius * 0.5))
                        {
                            Vec3d pos = capi.World.Player.Entity.Pos.XYZ.AddCopy(new Vec3d(x, y, z));
                            bool deathWaypoint = capi.World.Rand.NextDouble() > 0.8;

                            string icon = deathWaypoint ? "rocks" : iconKeys[(int)(capi.World.Rand.NextDouble() * (iconKeys.Length - 1))];
                            string title = deathWaypoint ? "Limits Test Player Death Waypoint" : "Limits Test Waypoint";

                            string str = string.Format(CultureInfo.InvariantCulture, "/waypoint addati {0} ={1} ={2} ={3} {4} #{5} {6}", icon, pos.X, pos.Y, pos.Z, deathWaypoint, ColorStuff.RandomHexColorVClamp(capi, 0.5, 0.8), title);

                            capi.SendChatMessage(str);
                            i--;
                        }
                        else return;

                        if (i < 1)
                        {
                            capi.Event.UnregisterGameTickListener(bulkActionID);
                            firstOpen = true;
                            bulkProcessing = false;
                            bulkActionID = 0;
                        }
                    }, 1);
                    break;
                default:
                    capi.ShowChatMessage(Lang.Get("Syntax: .wpcfg [dotrange|titlerange|perblockwaypoints|purge|waypointprefix|waypointid|enableall|import|export]"));
                    break;
            }
            cL.SaveConfig();
        }

        
        private bool ViewWaypoints(KeyCombination t1)
        {
            firstOpen = Config.FloatyWaypoints = !Config.FloatyWaypoints;
            return true;
        }
        bool firstOpen = true;
        public bool bulkProcessing = false;

        public void Update()
        {
            if (bulkActionID != 0 || bulkProcessing) return;

            if (Config.FloatyWaypoints)
            {
                bool repopped;

                if (repopped = WaypointElements.Count == 0) RepopulateDialogs();
                if (Waypoints.Count > 0 && (Waypoints.Count != WaypointElements.Count || firstOpen))
                {
                    if (!repopped) RepopulateDialogs();

                    foreach (var val in WaypointElements)
                    {
                        if (val.IsOpened() && (val.distance > Config.DotRange || Config.DisabledColors.Contains(val.waypoint.Color)) && (!val.DialogTitle.Contains("*") || val.waypoint.OwnWaypoint.Pinned)) val.TryClose();
                        else if (!val.IsOpened() && (val.distance < Config.DotRange && !Config.DisabledColors.Contains(val.waypoint.Color)) || (val.DialogTitle.Contains("*") || val.waypoint.OwnWaypoint.Pinned)) val.TryOpen();
                    }
                    firstOpen = false;
                }
                ManageZDepth();
            }
            else
            {
                foreach (var val in WaypointElements)
                {
                    if (val.IsOpened())
                    {
                        val.TryClose();
                    }
                }
            }
        }

        public void RepopulateDialogs()
        {
            foreach (var val in WaypointElements)
            {
                val.TryClose();
            }
            int elem = WaypointElements.Count;
            for (int i = 0; i < elem; i++)
            {
                WaypointElements[i] = null;
            }
            WaypointElements.Clear();

            foreach (var val in WaypointsRel)
            {
                HudElementWaypoint waypoint = new HudElementWaypoint(capi, val);
                waypoint.OnOwnPlayerDataReceived();

                WaypointElements.Add(waypoint);
            }
        }

        public bool PurgeWaypointsByStrings(params string[] containsStrings)
        {
            int i = Waypoints.Count - 1;
            bulkProcessing = true;
            bulkActionID = capi.Event.RegisterGameTickListener(dt =>
            {
                bool contains = false;
                foreach (var contained in containsStrings)
                {
                    if (contains = Waypoints[i].Title.Contains(contained)) break;
                }
                if (contains)
                {
                    capi.SendChatMessage("/waypoint remove " + i);
                    BlockPos rel = Waypoints[i].Position.AsBlockPos.SubCopy(capi.World.DefaultSpawnPosition.AsBlockPos);
                    string str = Waypoints[i].Title + " Deleted, Rel: " + rel.ToString() + ", Abs: " + Waypoints[i].Position.ToString();
                    StringBuilder builder = new StringBuilder(str).AppendLine();
                    FileInfo info = new FileInfo(Path.Combine(GamePaths.Logs, "waypoints-log.txt"));
                    GamePaths.EnsurePathExists(info.Directory.FullName);
                    File.AppendAllText(info.FullName, builder.ToString());
                    capi.Logger.Event(str);
                }
                if (i < 1)
                {
                    capi.Event.UnregisterGameTickListener(bulkActionID);
                    firstOpen = true;
                    bulkProcessing = false;
                    bulkActionID = 0;
                }
                else i--;
            }, 1);

            return true;
        }

        long bulkActionID;
        private void Purge()
        {
            bulkProcessing = true;
            int i = Waypoints.Count;
            bulkActionID = capi.Event.RegisterGameTickListener(dt =>
            {
                capi.SendChatMessage("/waypoint remove 0");
                i--;
                if (i < 1)
                {
                    capi.Event.UnregisterGameTickListener(bulkActionID);
                    firstOpen = true;
                    bulkProcessing = false;
                    bulkActionID = 0;
                }
            }, 1);
        }

        public void ManageZDepth()
        {
            var ordered = WaypointElements.OrderBy(wp => wp.DistanceFromPlayer).ToArray();
            float ZDepth = 0;
            for (int i = 0; i < ordered.Count(); i++)
            {
                float size = ordered[i].Focused ? 0 : ZDepth;
                ordered[i].ZDepth = size;
                ordered[i].order = i;
                ZDepth += 0.00001f;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            capi.World.UnregisterGameTickListener(updateID);
            capi.World.UnregisterGameTickListener(bulkActionID);
        }
    }

    public class WaypointRelative : Waypoint
    {
        public WaypointMapLayer WPLayer { get => api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<WaypointMapLayer>().Single(); }
        public List<Waypoint> Waypoints { get => WPLayer.ownWaypoints; }

        ICoreAPI api;
        public WaypointRelative(ICoreAPI api)
        {
            this.api = api;
        }

        public Vec3d RelativeToSpawn { get => Position.SubCopy(api.World.DefaultSpawnPosition.XYZ); }
        public double? DistanceFromPlayer { get => (api as ICoreClientAPI)?.World.Player.Entity.Pos.DistanceTo(Position); }
        public int Index { get; set; }
        public int OwnColor { get => OwnWaypoint?.Color ?? 0; }
        public Waypoint OwnWaypoint { get => Index > Waypoints.Count - 1 ? null : Waypoints[Index]; }
    }

    public class DummyWaypoint
    {
        public string Icon { get; set; }
        public Vec3d Position { get; set; }
        public bool Pinned { get; set; }
        public int Color { get; set; }
        public string Title { get; set; }
    }
}
