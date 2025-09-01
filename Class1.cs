using System;
using System.Collections.Generic;
using System.IO;
using Exiled.API.Features;
using Exiled.Events.EventArgs;
using Exiled.Permissions.Extensions;
using Exiled.API.Features.Items;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LancerToolGun
{
    public class Plugin : Plugin<Config>
    {
        public override string Name => "LancerToolGun Ultimate";
        public override string Author => "Lancer Team";
        public override Version Version => new Version(3, 3, 0);

        private static int currentModeIndex = 0;
        private static Dictionary<Player, Stack<GameObject>> undoStacks = new Dictionary<Player, Stack<GameObject>>();
        private static Dictionary<Player, Stack<GameObject>> redoStacks = new Dictionary<Player, Stack<GameObject>>();

        private string[] Modes => this.Config.Modes;
        private string ConfigPath => Path.Combine(Paths.Plugins, "LancerToolGun", "config.yml");

        public override void OnEnabled()
        {
            LoadOrCreateConfig();
            Exiled.Events.Handlers.Player.Verified += OnVerified;
            Exiled.Events.Handlers.Player.Died += OnPlayerDeath;
            Exiled.Events.Handlers.Player.Shooting += OnPlayerShooting;
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            Exiled.Events.Handlers.Player.Verified -= OnVerified;
            Exiled.Events.Handlers.Player.Died -= OnPlayerDeath;
            Exiled.Events.Handlers.Player.Shooting -= OnPlayerShooting;
            base.OnDisabled();
        }

        private void LoadOrCreateConfig()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                File.WriteAllText(ConfigPath, serializer.Serialize(this.Config));
                Log.Info("[LancerToolGun] Конфиг создан автоматически!");
            }
            else
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var loadedConfig = deserializer.Deserialize<Config>(File.ReadAllText(ConfigPath));

                this.Config.IsEnabled = loadedConfig.IsEnabled;
                this.Config.Debug = loadedConfig.Debug;
                this.Config.Modes = loadedConfig.Modes;
                this.Config.BuildPrefab = loadedConfig.BuildPrefab;
                this.Config.PaintColor = loadedConfig.PaintColor;
                this.Config.GridSize = loadedConfig.GridSize;

                Log.Info("[LancerToolGun] Конфиг загружен!");
            }
        }

        private void OnVerified(VerifiedEventArgs ev)
        {
            if (!ev.Player.CheckPermission("LancerToolGun.Admin")) return;

            Item gun = Item.Create(ItemType.GunCOM18);
            ev.Player.AddItem(gun);

            ShowHint(ev.Player, "<color=#00FFFF>ToolGun активирован!</color>\n<color=#FFD700>Нажмите T для смены режима</color>", 5f);

            if (!undoStacks.ContainsKey(ev.Player)) undoStacks[ev.Player] = new Stack<GameObject>();
            if (!redoStacks.ContainsKey(ev.Player)) redoStacks[ev.Player] = new Stack<GameObject>();
        }

        private void OnPlayerDeath(DiedEventArgs ev)
        {
            if (!ev.Player.CheckPermission("LancerToolGun.Admin")) return;

            foreach (Item i in ev.Player.Items)
            {
                if (i.Type == ItemType.GunCOM18)
                {
                    ev.Player.RemoveItem(i);
                    break;
                }
            }

            undoStacks.Remove(ev.Player);
            redoStacks.Remove(ev.Player);
        }

        private void OnPlayerShooting(ShootingEventArgs ev)
        {
            if (!ev.Player.CheckPermission("LancerToolGun.Admin")) return;

            // Получаем камеру Unity игрока
            UnityEngine.Camera cam = ev.Player.GameObject.GetComponentInChildren<UnityEngine.Camera>();
            if (cam == null) return;

            // Смена режима по T
            if (Input.GetKeyDown(KeyCode.T))
            {
                currentModeIndex++;
                if (currentModeIndex >= Modes.Length) currentModeIndex = 0;
                ShowModeHUD(ev.Player);
            }

            HandleMode(ev.Player, Modes[currentModeIndex], cam);
        }

        private void HandleMode(Player player, string mode, UnityEngine.Camera cam)
        {
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 10f)) return;

            GameObject obj = hit.collider.gameObject;

            if (mode == "Build") BuildObject(player, SnapToGrid(hit.point));
            else if (mode == "Destroy") DestroyObject(player, obj);
            else if (mode == "Paint") PaintObject(player, obj);
            else if (mode == "Move") MoveObject(player, obj, SnapToGrid(hit.point + Vector3.up));
            else if (mode == "Rotate") RotateObject(player, obj);
            else if (mode == "Scale") ScaleObject(player, obj);
        }

        private void BuildObject(Player player, Vector3 position)
        {
            if (this.Config.BuildPrefab == null) return;
            GameObject obj = UnityEngine.Object.Instantiate(this.Config.BuildPrefab, position, Quaternion.identity);
            undoStacks[player].Push(obj);
            ShowHint(player, "<color=#00FF00>Объект построен!</color>");
        }

        private void DestroyObject(Player player, GameObject obj)
        {
            if (obj == null) return;
            undoStacks[player].Push(obj);
            UnityEngine.Object.Destroy(obj);
            ShowHint(player, "<color=#FF0000>Объект разрушен!</color>");
        }

        private void PaintObject(Player player, GameObject obj)
        {
            if (obj == null) return;
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend != null) rend.material.color = this.Config.PaintColor;
            ShowHint(player, "<color=#FFFF00>Объект окрашен!</color>");
        }

        private void MoveObject(Player player, GameObject obj, Vector3 position)
        {
            if (obj == null) return;
            obj.transform.position = position;
            ShowHint(player, "<color=#00FFFF>Объект перемещен!</color>");
        }

        private void RotateObject(Player player, GameObject obj) { /* Реализация вращения */ }
        private void ScaleObject(Player player, GameObject obj) { /* Реализация масштаба */ }

        private Vector3 SnapToGrid(Vector3 position)
        {
            float grid = this.Config.GridSize;
            return new Vector3(
                Mathf.Round(position.x / grid) * grid,
                Mathf.Round(position.y / grid) * grid,
                Mathf.Round(position.z / grid) * grid
            );
        }

        private void ShowHint(Player player, string message, float time = 2f)
        {
            player.ShowHint(message + "\n<size=12><color=#AAAAAA>Сделано By Lancer Team</color></size>", time);
        }

        private void ShowModeHUD(Player player)
        {
            ShowHint(player, "ToolGun режим: " + Modes[currentModeIndex], 3f);
        }
    }
}