﻿using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.Elements.AtlasElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.PoEMemory.Components;
using SharpDX;
using GameOffsets.Components;
using ExileCore.Shared.Enums;
using System.Linq;
using System.Diagnostics;
using ExileCore.PoEMemory.Elements;
using System;

namespace AutoSextant;

public class AutoSextant : BaseSettingsPlugin<AutoSextantSettings>
{
    internal static AutoSextant Instance;

    public CompassList CompassList = new CompassList();
    public Dictionary<string, PoEStack.PoeStackPrice> Prices { get; set; } = new Dictionary<string, PoEStack.PoeStackPrice>();

    public override bool Initialise()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        Input.RegisterKey(Settings.RestockHotkey);
        Settings.RestockHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.RestockHotkey); };
        Input.RegisterKey(Settings.DumpHotkey);
        Settings.DumpHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.RestockHotkey); };
        Input.RegisterKey(Settings.RunHotkey);
        Settings.RunHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.RestockHotkey); };

        // SellAssistant.SellAssistant.Enable();

        var priceFetcher = new PoEStack.PriceFetcher();
        var task = priceFetcher.Load();
        task.ContinueWith((x) =>
        {
            Prices = x.Result;
            CompassList.Prices.Clear();
            foreach (var price in Prices.Values)
            {
                CompassList.Prices.Add(price.Name, new CompassPrice
                {
                    Name = price.Name,
                    ChaosPrice = (int)price.Value,
                    DivinePrice = 0
                });
            }
        });


        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        //Perform once-per-zone processing here
        //For example, Radar builds the zone map texture here
    }

    private string _restockCoroutineName = "AutoSextant_RestockCoroutine";
    private string _dumpCoroutineName = "AutoSextant_DumpCoroutine";
    private string _runCoroutineName = "AutoSextant_RunCoroutine";

    public bool IsAnyRoutineRunning
    {
        get
        {
            return Core.ParallelRunner.FindByName(_restockCoroutineName) != null ||
                   Core.ParallelRunner.FindByName(_dumpCoroutineName) != null ||
                   Core.ParallelRunner.FindByName(_runCoroutineName) != null ||
                   SellAssistant.SellAssistant.IsAnyRoutineRunning;
        }
    }

    public void StopAllRoutines()
    {
        var routine = Core.ParallelRunner.FindByName(_restockCoroutineName);
        routine?.Done();
        routine = Core.ParallelRunner.FindByName(_dumpCoroutineName);
        routine?.Done();
        routine = Core.ParallelRunner.FindByName(_runCoroutineName);
        routine?.Done();
        Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);
        Input.KeyUp(System.Windows.Forms.Keys.ControlKey);
        SellAssistant.SellAssistant.StopAllRoutines();
    }

    public override Job Tick()
    {
        if (Settings.RestockHotkey.PressedOnce())
        {
            SellAssistant.SellAssistant.Enable();
            // Core.ParallelRunner.Run(new Coroutine(Test(), this, _restockCoroutineName));
            // if (Core.ParallelRunner.FindByName(_restockCoroutineName) == null)
            // {
            //     Core.ParallelRunner.Run(new Coroutine(Restock(), this, _restockCoroutineName));
            // }
            // else
            // {
            //     StopAllRoutines();
            // }
        }
        if (Settings.DumpHotkey.PressedOnce())
        {
            if (Core.ParallelRunner.FindByName(_dumpCoroutineName) == null)
            {
                Core.ParallelRunner.Run(new Coroutine(Dump(), this, _dumpCoroutineName));
            }
            else
            {
                StopAllRoutines();
            }
        }
        if (Settings.RunHotkey.PressedOnce())
        {
            if (Core.ParallelRunner.FindByName(_runCoroutineName) == null)
            {
                Core.ParallelRunner.Run(new Coroutine(Run(), this, _runCoroutineName));
            }
            else
            {
                StopAllRoutines();
            }
        }

        SellAssistant.SellAssistant.Tick();
        return null;
    }

    private IEnumerator Test()
    {
        if (NStash.Stash.ActiveTab.Name == "Currency")
        {
            yield return NStash.Stash.SelectTab("CHARGED1");
        }
        else
        {
            yield return NStash.Stash.SelectTab("Currency");
        }
    }

    public IEnumerator Run()
    {
        yield return EnsureAtlas();

        if (!Atlas.HasBlockMods)
        {
            LogError("No block mods");
            yield break;
        }

        var stone = new VoidStone(VoidStonePosition.Top);
        var maxCharged = 7 * 5;

        var holdingShift = false;
        while (Inventory.TotalChargedCompasses < maxCharged)
        {
            var compassPrice = stone.Price;
            var currentName = compassPrice?.Name ?? null;

            if (compassPrice == null || compassPrice.ChaosPrice < Settings.MinChaosValue)
            {
                var nextSextant = Inventory.NextSextant;
                if (nextSextant == null)
                {
                    break;
                }
                if (!holdingShift)
                {
                    holdingShift = true;
                    Input.KeyDown(System.Windows.Forms.Keys.ShiftKey);
                    yield return Input.ClickElement(nextSextant.Position, System.Windows.Forms.MouseButtons.Right);
                }
                yield return Input.ClickElement(stone.Position);
                yield return new WaitFunctionTimed(() => stone.Price != null && stone.Price.Name != currentName, false, 50);
                if (stone.Price == null || stone.Price.Name == currentName)
                {
                    yield return new WaitTime(50);
                    // Didn't work or was the same, try again
                    continue;
                }
            }

            compassPrice = stone.Price;

            if (stone.Price != null && compassPrice.ChaosPrice >= Settings.MinChaosValue)
            {
                holdingShift = false;
                Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);
                var nextCompass = Inventory.NextCompass;
                var nextFreeSlot = Inventory.NextFreeChargedCompassSlot;
                if (nextCompass == null || nextFreeSlot == null)
                {
                    break;
                }
                yield return Input.ClickElement(nextCompass.Position, System.Windows.Forms.MouseButtons.Right);
                yield return Input.ClickElement(stone.Position);
                yield return Input.ClickElement(nextFreeSlot.Position);
            }
        }
        holdingShift = false;
        Input.KeyUp(System.Windows.Forms.Keys.ShiftKey);

        yield return Dump();
        yield return Restock();

        yield return Run();
    }

    public IEnumerator Restock()
    {
        yield return EnsureStash();

        var tab1 = Stash.GetStashTabIndexForName(Settings.RestockSextantFrom.Value);
        yield return NStash.Stash.SelectTab(tab1);

        yield return RestockColumn(0, ItemType.Compass);
        yield return RestockColumn(1, ItemType.Sextant);
        yield return RestockColumn(2, ItemType.Sextant);
        yield return RestockColumn(3, ItemType.Sextant);
    }

    public IEnumerator EnsureAtlas()
    {
        if (GameController.IngameState.IngameUi.Atlas.IsVisible && !GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            yield break;
        }
        yield return EnsureEverythingIsClosed();

        Input.KeyDown(Settings.AtlasHotKey.Value);
        Input.KeyUp(Settings.AtlasHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.Atlas.IsVisible, true, 1000, "Atlas not opened");

        Input.KeyDown(Settings.InventoryHotKey.Value);
        Input.KeyUp(Settings.InventoryHotKey.Value);

        yield return new WaitFunctionTimed(() => GameController.IngameState.IngameUi.InventoryPanel != null && GameController.IngameState.IngameUi.InventoryPanel.IsVisible, true, 1000, "Inventory not opened");
    }

    public IEnumerator Dump()
    {
        yield return EnsureStash();

        var dumpTabs = Settings.DumpTabs.Value.Split(',');

        while (Inventory.TotalChargedCompasses > 0)
        {
            foreach (var tabName in dumpTabs)
            {
                var items = Inventory.ChargedCompasses;
                var tab = Stash.GetStashTabIndexForName(tabName);
                yield return NStash.Stash.SelectTab(tab);
                yield return new WaitTime(30);
                var stashTabType = GameController.IngameState.IngameUi.StashElement.VisibleStash.InvType;
                var max = stashTabType == InventoryType.QuadStash ? 576 : 144;
                var count = GameController.IngameState.IngameUi.StashElement.VisibleStash.ItemCount;
                if (count >= max)
                {
                    continue;
                }
                var freeSlots = max - count;
                var toDump = Math.Min(freeSlots, items.Count());
                Input.KeyDown(System.Windows.Forms.Keys.ControlKey);
                for (int i = 0; i < toDump; i++)
                {
                    yield return Input.ClickElement(items[i].Position, 10);
                }
                Input.KeyUp(System.Windows.Forms.Keys.ControlKey);
            }
        }
    }

    private IEnumerator EnsureStash()
    {
        if (GameController.IngameState.IngameUi.StashElement.IsVisible && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            yield break;
        }
        yield return EnsureEverythingIsClosed();

        var itemsOnGround = GameController.IngameState.IngameUi.ItemsOnGroundLabels;
        var stash = GameController.IngameState.IngameUi.StashElement;

        if (stash is { IsVisible: true })
        {
            yield break;
        }

        foreach (LabelOnGround labelOnGround in itemsOnGround)
        {
            if (!labelOnGround.ItemOnGround.Path.Contains("/Stash"))
            {
                continue;
            }
            if (!labelOnGround.IsVisible)
            {
                LogError("Stash not visible");
                yield break;
            }
            yield return Input.ClickElement(labelOnGround.Label.GetClientRect().Center);
            yield return new WaitFunctionTimed(() => stash is { IsVisible: true }, true, 2000, "Stash not reached in time");
            if (stash is { IsVisible: false })
            {
                LogError("Stash not visible");
                yield break;
            }
        }
        yield return true;
    }

    private IEnumerator EnsureEverythingIsClosed()
    {
        if (GameController.IngameState.IngameUi.Atlas.IsVisible)
        {
            Input.KeyDown(Settings.AtlasHotKey.Value);
            Input.KeyUp(Settings.AtlasHotKey.Value);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.Atlas.IsVisible, true, 1000, "Atlas not closed");
        }
        if (GameController.IngameState.IngameUi.InventoryPanel != null && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            Input.KeyDown(Settings.InventoryHotKey.Value);
            Input.KeyUp(Settings.InventoryHotKey.Value);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.InventoryPanel.IsVisible, true, 1000, "Inventory not closed");
        }
        if (GameController.IngameState.IngameUi.StashElement != null && GameController.IngameState.IngameUi.StashElement.IsVisible)
        {
            Input.KeyDown(System.Windows.Forms.Keys.Escape);
            Input.KeyUp(System.Windows.Forms.Keys.Escape);
            yield return new WaitFunctionTimed(() => !GameController.IngameState.IngameUi.StashElement.IsVisible, true, 1000, "Inventory not closed");
        }
    }

    private IEnumerator RestockColumn(int col, ItemType type)
    {
        var compassItem = Stash.GetItemTypeFromStash(Item.ItemNames[type]).First();
        var itemWidth = compassItem.GetClientRect().Width / 56 * 70;
        var inventoryPanel = Instance.GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory];
        Vector2 inventoryPanelPosition = inventoryPanel.InventoryUIElement.GetClientRect().TopLeft;

        while (Inventory.CountItemsInColumn(col) + 10 <= 50)
        {
            var item = Stash.GetItemTypeFromStash(Item.ItemNames[type]).First();
            var itemStack = new Item(item);
            yield return itemStack.GetStack(true);
            yield return new WaitTime(10);
        }


        var column = Inventory.GetColumn(col);
        for (int i = 0; i < column.Count(); i++)
        {
            var slot = column[i];
            var item = Stash.GetItemTypeFromStash(Item.ItemNames[type]).First();
            var compassesStack = item.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>();
            var compassStackSize = compassesStack?.Size ?? 0;

            if (compassStackSize <= 0)
            {
                break;
            }

            Vector2 position;
            int sizeNeeded;
            if (slot == null)
            {
                sizeNeeded = 10;
                position = new Vector2(
                    inventoryPanelPosition.X + itemWidth / 2 + itemWidth * col,
                    inventoryPanelPosition.Y + itemWidth / 2 + itemWidth * i
                );
            }
            else
            {
                var slotStack = slot.Item.GetComponent<ExileCore.PoEMemory.Components.Stack>();
                var slotStackSize = slotStack?.Size ?? 0;
                sizeNeeded = 10 - slotStackSize;
                position = slot.GetClientRect().Center;
            }

            if (compassStackSize > sizeNeeded && sizeNeeded > 0)
            {
                var itemStack = new Item(item);
                if (sizeNeeded < 10)
                {
                    yield return itemStack.GetFraction(sizeNeeded);
                }
                else
                {
                    yield return itemStack.GetStack();
                }
                yield return Input.ClickElement(position);
            }
        }
    }

    public override void Render()
    {
        Error.Render();
        SellAssistant.SellAssistant.Render();
        if (Settings.PositionDebug.Value && GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
        {
            var free = Inventory.NextFreeChargedCompassSlot;
            if (free != null)
            {
                var newRect = new RectangleF(free.Position.X - 35, free.Position.Y - 35, 70, 70);
                Graphics.DrawFrame(newRect, Color.Green, 100f, 3, 0);
            }
        }
        var InnerAtlas = GameController.IngameState.IngameUi.Atlas.InnerAtlas;
        if (InnerAtlas.IsVisible)
        {
            VoidStone[] blockStones = {
                new(VoidStonePosition.Left),
                new(VoidStonePosition.Right),
                new(VoidStonePosition.Bottom)
            };

            foreach (var blockStone in blockStones)
            {
                var modName = blockStone.ModName;

                var isBlocked = modName != null &&
                                (Settings.UseModsForBlockingGroup1.Value.Contains(modName) ||
                                Settings.UseModsForBlockingGroup2.Value.Contains(modName) ||
                                Settings.UseModsForBlockingGroup3.Value.Contains(modName));

                var rect = blockStone.Slot.GetClientRect();
                // make new rect that is half the width and half the height and adjust the x and y to be in the center
                var newRect = new RectangleF(rect.X + rect.Width / 4, rect.Y + rect.Height / 4, rect.Width / 2,
                    rect.Height / 2);
                if (isBlocked)
                {
                    Graphics.DrawFrame(newRect, Color.Green, 100f, 3, 0);
                }
                else
                {
                    Graphics.DrawFrame(newRect, Color.Red, 100f, 3, 0);
                }
            }

            var rollingStone = new VoidStone(VoidStonePosition.Top);
            var compassPrice = rollingStone.Price;

            if (compassPrice != null)
            {
                var chaosPrice = compassPrice.ChaosPrice;
                var color = chaosPrice >= Settings.MinChaosValue ? Color.Green : Color.Red;
                Graphics.DrawFrame(rollingStone.Slot.GetClientRect(), color, 100f, 3, 0);
                var txt = $"{rollingStone.ClearName} - {chaosPrice} Chaos";
                var textSize = Graphics.MeasureText(txt, 20);
                var textPos = new System.Numerics.Vector2
                {
                    X = rollingStone.Slot.GetClientRect().Center.X - textSize.X / 2,
                    Y = rollingStone.Slot.GetClientRect().Top - 20
                };
                Graphics.DrawText(txt, textPos, color, 21);
            }
            else
            {
                Graphics.DrawFrame(rollingStone.Slot.GetClientRect(), Color.Red, 100f, 3, 0);
            }

        }
    }

    public override void EntityAdded(Entity entity)
    {
        //If you have a reason to process every entity only once,
        //this is a good place to do so.
        //You may want to use a queue and run the actual
        //processing (if any) inside the Tick method.
    }
}