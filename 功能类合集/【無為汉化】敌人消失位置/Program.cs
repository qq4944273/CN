﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace UniversalMinimapHack
{
    internal class Program
    {
        private static Program _instance;
        private string _version;
        private readonly IList<Position> _positions = new List<Position>();
        private MenuItem _slider;
        private MenuItem _ssFallbackPing;
        private MenuItem _ssCircle;
        private MenuItem _ssCircleColor;
        public MenuItem SsTimerEnabler { get; set; }

        private static void Main(string[] args)
        {
            GetInstance();
        }

        public static Program GetInstance()
        {
            if (_instance == null)
            {
                return new Program();
            }
            return _instance;
        }

        private Program()
        {
            _instance = this;
            CustomEvents.Game.OnGameLoad += Game_OnGameLoad;
        }

        private void Game_OnGameLoad(EventArgs args)
        {
            try
            {
                Menu menu = new Menu("【無為汉化】敌人消失位置", "UniversalMinimapHack", true);
                _slider = new MenuItem("scale", "图标比例 % (F5刷新图标)").SetValue(new Slider(20));
                IconOpacity = new MenuItem("opacity", "图标透明度 % (F5刷新图标)").SetValue(new Slider(70));
                SsTimerEnabler =
                    new MenuItem("enableSS", "启用").SetValue(true);
                SsTimerSize = new MenuItem("sizeSS", "SS尺寸（F5刷新图标)").SetValue(new Slider(15));
                SsTimerOffset = new MenuItem("offsetSS", "SS文本高度").SetValue(new Slider(15, -50, +50));
                SsTimerMin = new MenuItem("minSS", "在X秒显示").SetValue(new Slider(30, 1, 180));
                SsTimerMinPing = new MenuItem("minPingSS", "在X秒ping提醒").SetValue(new Slider(30, 5, 180));
                _ssFallbackPing = new MenuItem("fallbackSS", "撤退ping (本地)").SetValue(false);
                menu.AddItem(new MenuItem("", "[定制]"));
                menu.AddItem(_slider);
                menu.AddItem(IconOpacity);
                Menu ssMenu = new Menu("SS 定时器", "ssTimer");
                ssMenu.AddItem(SsTimerEnabler);
                ssMenu.AddItem(new MenuItem("1", "[额外的]"));
                ssMenu.AddItem(SsTimerMin);
                ssMenu.AddItem(_ssFallbackPing);
                ssMenu.AddItem(SsTimerMinPing);
                ssMenu.AddItem(new MenuItem("2", "[定制]"));
                ssMenu.AddItem(SsTimerSize);
                ssMenu.AddItem(SsTimerOffset);
                Menu ssCircleMenu = new Menu("SS 圈", "ccCircles");
                _ssCircle = new MenuItem("ssCircle", "启用").SetValue(true);
                SsCircleSize = new MenuItem("ssCircleSize", "圈的大小").SetValue(new Slider(7000, 500, 15000));
                _ssCircleColor = new MenuItem("ssCircleColor", "颜色").SetValue(System.Drawing.Color.Green);
                ssCircleMenu.AddItem(_ssCircle);
                ssCircleMenu.AddItem(SsCircleSize);
                ssCircleMenu.AddItem(_ssCircleColor);
                menu.AddSubMenu(ssMenu);
                menu.AddSubMenu(ssCircleMenu);
                menu.AddToMainMenu();


                var attempt = 0;
                _version = GameVersion();
                while (string.IsNullOrEmpty(_version) && attempt < 5)
                {
                    _version = GameVersion();
                    attempt++;
                }

                if (!string.IsNullOrEmpty(_version))
                {
                    LoadImages();
                    Print("Loaded!");
                    Game.OnGameUpdate += Game_OnGameUpdate;
                    Drawing.OnDraw += Drawing_OnDraw;
                    Drawing.OnEndScene += Drawing_OnEndScene;
                    Drawing.OnPreReset += Drawing_OnPreReset;
                    Drawing.OnPostReset += Drawing_OnPostReset;
                }
                else
                {
                    Print("Failed to load ddragon version after " + attempt + 1 + " attempts!");
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("[ERROR] " + e.ToString());
                Print("[ERROR] " + e.ToString());
            }
        }

        private void Drawing_OnDraw(EventArgs args)
        {

        }

        private void Drawing_OnPostReset(EventArgs args)
        {
            foreach (Position pos in _positions)
            {
                pos.Text.OnPostReset();
            }
        }

        private void Drawing_OnPreReset(EventArgs args)
        {
            foreach (Position pos in _positions)
            {
                pos.Text.OnPreReset();
            }
        }

        private void Drawing_OnEndScene(EventArgs args)
        {
            foreach (Position pos in _positions)
            {
                if (!pos.Hero.IsVisible && !pos.Hero.IsDead)
                {
                    float radius = Math.Abs(pos.LastLocation.X - pos.PredictedLocation.X);
                    if (radius < SsCircleSize.GetValue<Slider>().Value && _ssCircle.GetValue<bool>())
                    {
                        Utility.DrawCircle(pos.LastLocation, radius, _ssCircleColor.GetValue<System.Drawing.Color>(), 1, 30, true);
                    }

                }
                if (pos.Text.Visible)
                {
                    pos.Text.OnEndScene();
                }
            }
        }

        private void Game_OnGameUpdate(EventArgs args)
        {
            foreach (Position pos in _positions)
            {
                if (pos.Hero.ServerPosition != pos.LastLocation && pos.Hero.ServerPosition != pos.BeforeRecallLocation)
                {
                    pos.LastLocation = pos.Hero.ServerPosition;
                    pos.PredictedLocation = pos.Hero.ServerPosition;
                    pos.LastSeen = Game.ClockTime;
                }

                if (!pos.Hero.IsVisible && pos.RecallStatus != Packet.S2C.Recall.RecallStatus.RecallStarted)
                {
                    pos.PredictedLocation = new Vector3(pos.LastLocation.X + ((Game.ClockTime - pos.LastSeen) * pos.Hero.MoveSpeed), pos.LastLocation.Y, pos.LastLocation.Z);
                }

                if (pos.Hero.IsVisible && !pos.Hero.IsDead)
                {
                    pos.Pinged = false;
                    pos.LastSeen = Game.ClockTime;
                }


                if (pos.LastSeen >  0f && _ssFallbackPing.GetValue<bool>() && !pos.Hero.IsVisible)
                {
                    if (Game.ClockTime - pos.LastSeen >= SsTimerMinPing.GetValue<Slider>().Value && !pos.Pinged)
                    {
                        Packet.S2C.Ping.Encoded(new Packet.S2C.Ping.Struct(pos.LastLocation.X, pos.LastLocation.Y, pos.Hero.NetworkId,
                            ObjectManager.Player.NetworkId, Packet.PingType.EnemyMissing)).Process();
                        pos.Pinged = true;
                    }
                }

            }
        }

        private float GetScale()
        {
            return _slider.GetValue<Slider>().Value / 100f;
        }

        private void LoadImages()
        {
            foreach (
                Obj_AI_Hero hero in
                    ObjectManager.Get<Obj_AI_Hero>()
                        .Where(hero => hero != null && hero.Team != ObjectManager.Player.Team && hero.IsValid))
            {
                LoadImage(hero);
            }
        }

        private void LoadImage(Obj_AI_Hero hero)
        {
            Bitmap bmp = null;
            if (File.Exists(GetImageCached(hero.ChampionName)))
            {
                bmp = new Bitmap(GetImageCached(hero.ChampionName));
            }
            else
            {
                int attempt = 0;
                Bitmap tmp = DownloadImage(hero.ChampionName);
                while (tmp == null && attempt < 5)
                {
                    tmp = DownloadImage(hero.ChampionName);
                    attempt++;
                }

                if (tmp == null)
                {
                    Print("Failed to load " + hero.ChampionName + " after " + attempt + 1 + " attempts!");
                }
                else
                {
                    bmp = CreateFinalImage(tmp, 0, 0, tmp.Width);
                    bmp.Save(GetImageCached(hero.ChampionName));
                    tmp.Dispose();
                }
            }

            if (bmp != null)
            {
                Position pos = new Position(hero, ChangeOpacity(bmp, IconOpacity.GetValue<Slider>().Value / 100f),
                    GetScale());
                _positions.Add(pos);
            }
        }

        private Bitmap DownloadImage(string champName)
        {
            WebRequest request =
                WebRequest.Create("http://ddragon.leagueoflegends.com/cdn/" + _version + "/img/champion/" + champName +
                                  ".png");
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }
                Stream responseStream;
                using (responseStream = response.GetResponseStream())
                {
                    return responseStream != null && responseStream != Stream.Null ? new Bitmap(responseStream) : null;
                }

            }
        }

        public Bitmap CreateFinalImage(Bitmap srcBitmap, int circleUpperLeftX, int circleUpperLeftY, int circleDiameter)
        {
            Bitmap finalImage = new Bitmap(circleDiameter, circleDiameter);
            System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(circleUpperLeftX, circleUpperLeftY,
                circleDiameter, circleDiameter);

            using (Bitmap sourceImage = srcBitmap)
            using (Bitmap croppedImage = sourceImage.Clone(cropRect, sourceImage.PixelFormat))
            using (TextureBrush tb = new TextureBrush(croppedImage))
            using (Graphics g = Graphics.FromImage(finalImage))
            {
                g.FillEllipse(tb, 0, 0, circleDiameter, circleDiameter);
                Pen p = new Pen(System.Drawing.Color.DarkRed, 10) { Alignment = PenAlignment.Inset };
                g.DrawEllipse(p, 0, 0, circleDiameter, circleDiameter);
            }
            return finalImage;
        }

        public static Bitmap ChangeOpacity(Bitmap img, float opacityvalue)
        {
            Bitmap bmp = new Bitmap(img.Width, img.Height); // Determining Width and Height of Source Image
            Graphics graphics = Graphics.FromImage(bmp);
            ColorMatrix colormatrix = new ColorMatrix { Matrix33 = opacityvalue };
            ImageAttributes imgAttribute = new ImageAttributes();
            imgAttribute.SetColorMatrix(colormatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            graphics.DrawImage(img, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, img.Width,
                img.Height, GraphicsUnit.Pixel, imgAttribute);
            graphics.Dispose(); // Releasing all resource used by graphics
            img.Dispose();
            return bmp;
        }

        private void Print(string msg)
        {
            Game.PrintChat(
                "<font color='#ff3232'>Universal</font><font color='#BABABA'>MinimapHack:</font> <font color='#FFFFFF'>" +
                msg + "</font>");
        }

        public string GameVersion()
        {
            String json = new WebClient().DownloadString("http://ddragon.leagueoflegends.com/realms/euw.json");
            return (string)new JavaScriptSerializer().Deserialize<Dictionary<String, Object>>(json)["v"];
        }

        public string GetImageCached(string champName)
        {
            string path = Path.GetTempPath() + "UniversalMinimapHack";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            path += "\\" + _version;
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path + "\\" + champName + ".png";
        }


        public MenuItem SsTimerSize { get; set; }

        public MenuItem SsTimerOffset { get; set; }

        public MenuItem IconOpacity { get; set; }

        public MenuItem SsTimerMin { get; set; }

        public MenuItem SsTimerMinPing { get; set; }

        public MenuItem SsCircleSize { get; set; }
    }


    public class Position
    {
        private static int _layer;

        public Render.Sprite Image { get; set; }
        public Render.Text Text { get; set; }
        public Render.Circle Circle { get; set; }
        public Obj_AI_Hero Hero { get; set; }
        public Packet.S2C.Recall.RecallStatus RecallStatus { get; set; }

        public float LastSeen { get; set; }
        public Vector3 LastLocation { get; set; }
        public Vector3 PredictedLocation { get; set; }
        public Vector3 BeforeRecallLocation { get; set; }
        public bool Pinged { get; set; }



        public Position(Obj_AI_Hero hero, Bitmap bmp, float scale)
        {
            Hero = hero;
            Image = new Render.Sprite(bmp, new Vector2(0, 0));
            Image.GrayScale();
            Image.Scale = new Vector2(scale, scale);
            Image.VisibleCondition = sender => !hero.IsVisible && !hero.IsDead;
            Image.PositionUpdate = delegate
            {
                Vector2 v2 = Drawing.WorldToMinimap(LastLocation);
                v2.X -= Image.Width / 2f;
                v2.Y -= Image.Height / 2f;
                return v2;
            };
            Image.Add(_layer);
            LastSeen = 0;
            LastLocation = hero.ServerPosition;
            PredictedLocation = hero.ServerPosition;
            BeforeRecallLocation = hero.ServerPosition;

            Text = new Render.Text(0, 0, "", Program.GetInstance().SsTimerSize.GetValue<Slider>().Value,
                SharpDX.Color.White)
            {
                VisibleCondition =
                    sender =>
                        !hero.IsVisible && !Hero.IsDead && Program.GetInstance().SsTimerEnabler.GetValue<bool>() && LastSeen > 20f &&
                        Program.GetInstance().SsTimerMin.GetValue<Slider>().Value <= Game.ClockTime - LastSeen,
                PositionUpdate = delegate
                {
                    Vector2 v2 = Drawing.WorldToMinimap(LastLocation);
                    v2.Y += Program.GetInstance().SsTimerOffset.GetValue<Slider>().Value;
                    return v2;
                },
                TextUpdate = () => Format(Game.ClockTime - LastSeen),
                OutLined = true,
                Centered = true
            };
            Text.Add(_layer);

            _layer++;

            Game.OnGameProcessPacket += Game_OnGameProcessPacket;
        }

        private void Game_OnGameProcessPacket(GamePacketEventArgs args)
        {
            if (args.PacketData[0] == Packet.S2C.Recall.Header)
            {
                Packet.S2C.Recall.Struct decoded = Packet.S2C.Recall.Decoded(args.PacketData);
                if (decoded.UnitNetworkId == Hero.NetworkId)
                {
                    RecallStatus = decoded.Status;
                    if (decoded.Status == Packet.S2C.Recall.RecallStatus.RecallFinished)
                    {
                        BeforeRecallLocation = Hero.ServerPosition;
                        Vector3 enemyPos =
                            ObjectManager.Get<GameObject>()
                                .First(
                                    x => x.Type == GameObjectType.obj_SpawnPoint && x.Team != ObjectManager.Player.Team)
                                .Position;
                        LastLocation = enemyPos;
                        PredictedLocation = enemyPos;
                        LastSeen = Game.ClockTime;
                    }
                }
            }
        }

        private string Format(float f)
        {
            TimeSpan t = TimeSpan.FromSeconds(f);
            if (t.Minutes < 1) return t.Seconds + "";
            if (t.Seconds >= 10)
            {
                return t.Minutes + ":" + t.Seconds;
            }
            return t.Minutes + ":0" + t.Seconds;
        }
    }
}
