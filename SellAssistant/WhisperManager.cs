using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;
using ExileCore.Shared.Nodes;
using ImGuiNET;

namespace AutoSextant.SellAssistant;

public enum FullfillmentStatus
{
    Available,
    NotEnough,
    NotAvailable
}

public static class WhisperManager
{
    public static List<Whisper> Whispers = Chat.GetPastMessages("@From", Whisper.WhisperPatterns.Select(x => new Regex(x.Item1)).ToList(), 3).Select(Whisper.Create).Where(x => x != null).ToList();
    // public static List<Whisper> Whispers = new List<Whisper>
    // {
    //     Whisper.Create("@From _______test___________________: wtb 80 chayula"),
    //     Whisper.Create("@From _____Test_____________: WTB 300 Strongbox Enraged 274c each, 4 Beyond 74c each, 600 Resonating Shrine 88c each. Total 3828c (16 div + 100c)"),
    // }.Where(x => x != null).ToList();//.Select(x => { x.InArea = true; return x; }).ToList();

    public static List<Whisper> ActiveWhispers
    {
        get
        {
            return Whispers.Where(x => !x.Hidden).ToList();
        }
    }

    private static string chatPointer = Chat.GetPointer();
    private static ThrottledAction _chatUpdate = new ThrottledAction(TimeSpan.FromMilliseconds(500), () =>
    {
        foreach (var message in Chat.NewMessages(chatPointer))
        {
            if (message.StartsWith("@From"))
            {
                var whisper = Whisper.Create(message);
                if (whisper != null)
                {
                    ExecuteOnNextTick(() =>
                    {
                        Whispers.Add(whisper);
                        if (ActiveWhispers.Count == 1)
                            HotKeySelected = whisper.Uuid;
                    });
                }
            }

            if (message.Contains("has joined the area"))
            {
                string pattern = "(<.*> )?(.*) has joined the area";
                Match match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string username = match.Groups[2].Value;
                    // update all whispers with this username
                    foreach (var whisper in Whispers.Where(x => x.PlayerName == username))
                    {
                        whisper.InArea = true;
                    }
                }
            }
            if (message.Contains("has left the area"))
            {
                string pattern = "(<.*> )?(.*) has left the area";
                Match match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string username = match.Groups[2].Value;
                    // update all whispers with this username
                    foreach (var whisper in Whispers.Where(x => x.PlayerName == username))
                    {
                        whisper.InArea = false;
                    }
                }
            }

            foreach (var w in ActiveWhispers)
            {
                if (message.Contains(w.PlayerName))
                {
                    w.Messages.Add(message);
                }
            }
        }
    });

    private static bool _hotKeysRegistered = false;
    private static Dictionary<int, HotkeyNode> _hotkeys = new Dictionary<int, HotkeyNode>();
    public static string HotKeySelected = "";

    private static List<Action> _executeOnNextTick = new List<Action>();
    public static void ExecuteOnNextTick(Action action)
    {
        lock (_executeOnNextTick)
            _executeOnNextTick.Add(action);
    }
    public static void Tick()
    {
        _executeOnNextTick.ForEach(x => x.Invoke());
        _executeOnNextTick.Clear();

        _chatUpdate.Run();

        if (!_hotKeysRegistered)
        {
            for (int i = 0; i < 10; i++)
            {
                var key = Util.MapIndexToNumPad(i);
                Input.RegisterKey(key);
                _hotkeys[i] = new HotkeyNode(Util.MapIndexToNumPad(i));
            }
            Input.RegisterKey(System.Windows.Forms.Keys.Down);
            _hotkeys[10] = new HotkeyNode(System.Windows.Forms.Keys.Down);
            Input.RegisterKey(System.Windows.Forms.Keys.Up);
            _hotkeys[11] = new HotkeyNode(System.Windows.Forms.Keys.Up);
            _hotKeysRegistered = true;
        }

        var selected = Whispers.Find(x => x.Uuid == HotKeySelected);
        if (selected != null && selected.Hidden)
        {
            HotKeySelected = "";
            return;
        }

        if (SellAssistant.IsAmountFocussed)
            return;

        if (_hotkeys[0].PressedOnce())
        {
            if (selected != null && selected.ButtonSelection != ButtonSelection.None)
                selected.ButtonSelection = ButtonSelection.None;
            else if (HotKeySelected == "" && ActiveWhispers.Count > 0)
                HotKeySelected = ActiveWhispers[0].Uuid;
            else
                HotKeySelected = "";
        }
        if (_hotkeys[10].PressedOnce())
        {
            // down button pressed, if selected is null, select first whisper, otherwise select next whisper
            if (selected == null)
            {
                if (ActiveWhispers.Count > 0)
                    HotKeySelected = ActiveWhispers[0].Uuid;
                else
                {
                    var index = ActiveWhispers.IndexOf(selected);
                    if (index < ActiveWhispers.Count - 1)
                        HotKeySelected = ActiveWhispers[index + 1].Uuid;
                }
            }
        }
        if (_hotkeys[11].PressedOnce())
        {
            // up button pressed, if selected is null, select last whisper, otherwise select previous whisper
            if (selected == null)
            {
                if (ActiveWhispers.Count > 0)
                    HotKeySelected = ActiveWhispers[ActiveWhispers.Count - 1].Uuid;
                else
                {
                    var index = ActiveWhispers.IndexOf(selected);
                    if (index > 0)
                        HotKeySelected = ActiveWhispers[index - 1].Uuid;
                }
            }
        }

        var whispers = ActiveWhispers;
        if (selected == null)
        {
            for (int i = 1; i <= whispers.Count; i++)
            {
                if (_hotkeys[i].PressedOnce())
                {
                    HotKeySelected = whispers[i - 1].Uuid;
                    break;
                }
            }
        }
        else
        {
            var actions = selected.GetButtonsForWhisper();
            foreach (var (key, _, action) in actions)
            {
                if (_hotkeys[Util.MapNumPadToIndex(key)].PressedOnce())
                {
                    action.Invoke();
                    break;
                }
            }
        }
    }

    private static void ClearWhispers()
    {
        Whispers.Clear();
    }

    public static void Render()
    {
        if (ImGui.BeginTable("WhisperLastChatterAddTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableNextRow();
            for (int i = 0; i < Chat.LastChatUsers.Count; i++)
            {
                ImGui.TableNextColumn();
                var username = Chat.LastChatUsers[i];
                if (ImGui.Selectable(username, false))
                {
                    _executeOnNextTick.Add(() =>
                    {
                        Whispers.Add(new Whisper
                        {
                            Uuid = Guid.NewGuid().ToString(),
                            PlayerName = username,
                        });
                    });

                }
            }
            ImGui.EndTable();
        }

        float tableWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginTable("WhisperManagerTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("Buyer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, tableWidth * 0.07f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, tableWidth * 0.1f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (int col = 0; col < 6; ++col)
            {
                ImGui.TableNextColumn();
                if (col == 5) // Add button to last header column
                {
                    float buttonHeight = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y;
                    if (ImGui.Button("Clear", new Vector2(0, buttonHeight)))  // Width 0 means auto-sizing to fit text
                        _executeOnNextTick.Add(() => ClearWhispers());
                }
                else
                {
                    ImGui.Text(col == 0 ? "Buyer" : col == 1 ? "Name" : col == 2 ? "Amount" : col == 3 ? "Value" : "Status");
                }
            }

            var whispers = ActiveWhispers;
            for (int i = 0; i < whispers.Count; i++)
            {
                var whisper = whispers[i];

                ImGui.PushID(i);

                ImGui.TableNextRow();

                if (whisper.InArea)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0, 1, 0, 0.3f)));
                }

                ImGui.TableNextColumn();
                if (ImGui.Selectable(whisper.PlayerName))
                {
                    _executeOnNextTick.Add(() =>
                    {
                        Chat.ChatWith(whisper.PlayerName);
                    });
                }
                ImGui.TextDisabled("(Original message)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginItemTooltip();//BeginTooltip();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                    foreach (var m in whisper.Messages)
                        ImGui.TextUnformatted(m);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
                if (whisper.ProbablyWantsChange)
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), "(Change)");


                ImGui.TableNextColumn();
                whisper.Items.ForEach(x =>
                {
                    ImGui.PushID(i.ToString() + x.Name);
                    float buttonHeight = ImGui.GetFontSize();
                    if (ImGui.Button("S", new Vector2(buttonHeight, buttonHeight)))
                    {
                        _executeOnNextTick.Add(() =>
                        {
                            HotKeySelected = whisper.Uuid;
                        });
                        SellAssistant.ExecuteOnNextTick(() =>
                        {
                            SellAssistant.selectedFocus = true;
                            SellAssistant.selectedAmount = x.Quantity;
                            SellAssistant.selectedMod = x.ModName;
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("X", new Vector2(buttonHeight, buttonHeight)))
                    {
                        _executeOnNextTick.Add(() => whisper.RemoveItem(x.Uuid));
                    }
                    ImGui.SameLine();
                    ImGui.Text(x.Name);

                    ImGui.PopID();
                });
                ImGui.TableNextColumn();
                whisper.Items.ForEach(x =>
                {
                    if (x.Extracted == 0)
                        ImGui.TextColored(new System.Numerics.Vector4(1, 1, 1, 1), x.Extracted + "/" + x.Quantity.ToString());
                    else if (x.Extracted < x.Quantity)
                        ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), x.Extracted + "/" + x.Quantity.ToString());
                    else
                        ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), x.Extracted + "/" + x.Quantity.ToString());
                });
                ImGui.TableNextColumn();
                whisper.Items.ForEach(x => ImGui.Text(x.PriceFormatted));
                if (whisper.MultiItem)
                {
                    ImGui.Text("-----------------");
                    ImGui.Text(whisper.Price);
                }
                if (whisper.ValueReceived > 0)
                {
                    if (whisper.ValueReceived < whisper.TotalPrice)
                        ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), whisper.ValueReceivedFormatted);
                    else
                        ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), whisper.ValueReceivedFormatted);
                }

                ImGui.TableNextColumn();
                whisper.Items.ForEach(x =>
                {
                    var status = x.Status;

                    // display red or green or orange
                    if (status == FullfillmentStatus.Available)
                        ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Ok");
                    else if (status == FullfillmentStatus.NotEnough)
                        ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), "Partial");
                    else
                        ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), "N/A");
                });

                ImGui.TableNextColumn();

                float buttonWidth = ImGui.GetContentRegionAvail().X;
                var isSelected = whisper.Uuid == HotKeySelected;
                foreach (var (key, label, action) in whisper.GetButtonsForWhisper())
                {
                    if (ImGui.Button(isSelected ? $"[{Util.MapNumPadToIndex(key)}] {label}" : label, new Vector2(buttonWidth, 0)))
                    {
                        _executeOnNextTick.Add(action);
                    }
                }


                ImGui.PopID();
            }


            ImGui.EndTable();
        }
    }

    private static void GrayButton(bool condition)
    {
        if (condition)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetColorU32(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetColorU32(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 0.5f)));
        }
    }
    private static void GrayButtonEnd(bool condition)
    {
        if (condition)
        {
            ImGui.PopStyleColor(3);
        }
    }
}
