using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BetterSpire2;

public static class RestartTracker
{
    private static CharacterModel? _character;
    private static List<ActModel>? _acts;
    private static List<ModifierModel>? _modifiers;
    private static int _ascension;
    private static bool _restartPending;

    public static void RestartRun()
    {
        try
        {
            if (_restartPending) return;

            var instance = RunManager.Instance;
            if (instance == null) return;
            var state = Traverse.Create(instance).Property<RunState>("State").Value;
            if (state == null) return;

            _character = state.Players[0].Character;
            _acts = state.Acts.ToList();
            _modifiers = state.Modifiers.ToList();
            _ascension = state.AscensionLevel;
            _restartPending = true;

            TaskHelper.RunSafely(RestartRunAsync());
        }
        catch (Exception ex)
        {
            _restartPending = false;
            ModLog.Error("RestartTracker.RestartRun", ex);
        }
    }

    private static async Task RestartRunAsync()
    {
        try
        {
            var game = NGame.Instance;
            if (game == null) return;

            await game.ReturnToMainMenu();
            await game.ToSignal(game.GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

            // Stop main menu music before starting the new run
            try { game.AudioManager?.StopMusic(); }
            catch (Exception ex) { ModLog.Error("RestartRun StopMusic", ex); }

            string newSeed = SeedHelper.GetRandomSeed();
            var player = Player.CreateForNewRun(_character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
            var players = new List<Player> { player };
            var runState = RunState.CreateForNewRun(players, _acts, _modifiers, _ascension, newSeed);
            RunManager.Instance.SetUpNewSinglePlayer(runState, true);

            var startRunMethod = AccessTools.Method(typeof(NGame), "StartRun");
            var task = (Task)startRunMethod.Invoke(game, new object[] { runState });
            await task;
        }
        catch (Exception ex)
        {
            ModLog.Error("RestartRunAsync", ex);
        }
        finally
        {
            _restartPending = false;
        }
    }
}
