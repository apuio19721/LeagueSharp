﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace BrianSharp.Evade
{
    internal static class SkillshotDetector
    {
        public delegate void OnDeleteMissileH(Skillshot skillshot, Obj_SpellMissile missile);

        public delegate void OnDetectSkillshotH(Skillshot skillshot);

        public static List<Skillshot> DetectedSkillshots = new List<Skillshot>();

        static SkillshotDetector()
        {
            Obj_AI_Base.OnProcessSpellCast += OnProcessSpellCast;
            GameObject.OnCreate += ObjSpellMissileOnCreate;
            GameObject.OnDelete += ObjSpellMissileOnDelete;
            GameObject.OnDelete += ObjOnDelete;
        }

        private static void OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (!sender.IsValid<Obj_AI_Hero>() || sender.Team == ObjectManager.Player.Team)
            {
                return;
            }
            if (args.SData.Name == "dravenrdoublecast")
            {
                DetectedSkillshots.RemoveAll(
                    i => i.Unit.NetworkId == sender.NetworkId && i.SpellData.SpellName == "DravenRCast");
            }
            var spellData = SpellDatabase.GetByName(args.SData.Name);
            if (spellData == null)
            {
                return;
            }
            var startPos = new Vector2();
            if (spellData.FromObject != "")
            {
                foreach (
                    var obj in ObjectManager.Get<GameObject>().Where(obj => obj.Name.Contains(spellData.FromObject)))
                {
                    startPos = obj.Position.To2D();
                }
            }
            else
            {
                startPos = sender.ServerPosition.To2D();
            }
            if (spellData.FromObjects != null && spellData.FromObjects.Length > 0)
            {
                foreach (var obj in
                    ObjectManager.Get<GameObject>().Where(i => i.IsEnemy && spellData.FromObject.Contains(i.Name)))
                {
                    var start = obj.Position.To2D();
                    var end = start + spellData.Range * (args.End.To2D() - obj.Position.To2D()).Normalized();
                    TriggerOnDetectSkillshot(
                        DetectionType.ProcessSpell, spellData, Utils.GameTimeTickCount - Game.Ping / 2, start, end,
                        sender);
                }
            }
            if (!startPos.IsValid())
            {
                return;
            }
            var endPos = args.End.To2D();
            if (spellData.SpellName == "LucianQ" && args.Target != null &&
                args.Target.NetworkId == ObjectManager.Player.NetworkId)
            {
                return;
            }
            var direction = (endPos - startPos).Normalized();
            if (startPos.Distance(endPos) > spellData.Range || spellData.FixedRange)
            {
                endPos = startPos + direction * spellData.Range;
            }
            if (spellData.ExtraRange != -1)
            {
                endPos = endPos +
                         Math.Min(spellData.ExtraRange, spellData.Range - endPos.Distance(startPos)) * direction;
            }
            TriggerOnDetectSkillshot(
                DetectionType.ProcessSpell, spellData, Utils.GameTimeTickCount - Game.Ping / 2, startPos, endPos, sender);
        }

        private static void ObjSpellMissileOnCreate(GameObject sender, EventArgs args)
        {
            if (!sender.IsValid<Obj_SpellMissile>())
            {
                return;
            }
            var missile = (Obj_SpellMissile) sender;
            if (!missile.SpellCaster.IsValid<Obj_AI_Hero>() || missile.SpellCaster.Team == ObjectManager.Player.Team)
            {
                return;
            }
            var unit = (Obj_AI_Hero) missile.SpellCaster;
            var spellData = SpellDatabase.GetByMissileName(missile.SData.Name);
            if (spellData == null)
            {
                return;
            }
            var missilePosition = missile.Position.To2D();
            var unitPosition = missile.StartPosition.To2D();
            var endPos = missile.EndPosition.To2D();
            var direction = (endPos - unitPosition).Normalized();
            if (unitPosition.Distance(endPos) > spellData.Range || spellData.FixedRange)
            {
                endPos = unitPosition + direction * spellData.Range;
            }
            if (spellData.ExtraRange != -1)
            {
                endPos = endPos +
                         Math.Min(spellData.ExtraRange, spellData.Range - endPos.Distance(unitPosition)) * direction;
            }
            var castTime = Utils.GameTimeTickCount - Game.Ping / 2 - (spellData.MissileDelayed ? 0 : spellData.Delay) -
                           (int) (1000 * missilePosition.Distance(unitPosition) / spellData.MissileSpeed);
            TriggerOnDetectSkillshot(DetectionType.RecvPacket, spellData, castTime, unitPosition, endPos, unit);
        }

        private static void ObjSpellMissileOnDelete(GameObject sender, EventArgs args)
        {
            if (!sender.IsValid<Obj_SpellMissile>())
            {
                return;
            }
            var missile = (Obj_SpellMissile) sender;
            if (!missile.SpellCaster.IsValid<Obj_AI_Hero>() || missile.SpellCaster.Team == ObjectManager.Player.Team)
            {
                return;
            }
            var unit = (Obj_AI_Hero) missile.SpellCaster;
            var spellName = missile.SData.Name;
            if (OnDeleteMissile != null)
            {
                foreach (var skillshot in
                    DetectedSkillshots.Where(
                        i =>
                            i.SpellData.MissileSpellName == spellName && i.Unit.NetworkId == unit.NetworkId &&
                            (missile.EndPosition.To2D() - missile.StartPosition.To2D()).AngleBetween(i.Direction) < 10 &&
                            i.SpellData.CanBeRemoved))
                {
                    OnDeleteMissile(skillshot, missile);
                    break;
                }
            }
            DetectedSkillshots.RemoveAll(
                i =>
                    (i.SpellData.MissileSpellName == spellName || i.SpellData.ExtraMissileNames.Contains(spellName)) &&
                    i.Unit.NetworkId == unit.NetworkId &&
                    (missile.EndPosition.To2D() - missile.StartPosition.To2D()).AngleBetween(i.Direction) < 10 &&
                    (i.SpellData.CanBeRemoved || i.SpellData.ForceRemove));
        }

        private static void ObjOnDelete(GameObject sender, EventArgs args)
        {
            if (!sender.IsValid || sender.Team == ObjectManager.Player.Team)
            {
                return;
            }
            for (var i = DetectedSkillshots.Count - 1; i >= 0; i--)
            {
                var skillshot = DetectedSkillshots[i];
                if (skillshot.SpellData.ToggleParticleName != "" &&
                    new Regex(skillshot.SpellData.ToggleParticleName).IsMatch(sender.Name))
                {
                    DetectedSkillshots.RemoveAt(i);
                }
            }
        }

        public static event OnDetectSkillshotH OnDetectSkillshot;
        public static event OnDeleteMissileH OnDeleteMissile;

        private static void TriggerOnDetectSkillshot(DetectionType detectionType,
            SpellData spellData,
            int startT,
            Vector2 start,
            Vector2 end,
            Obj_AI_Base unit)
        {
            var skillshot = new Skillshot(detectionType, spellData, startT, start, end, unit);
            if (OnDetectSkillshot != null)
            {
                OnDetectSkillshot(skillshot);
            }
        }
    }
}