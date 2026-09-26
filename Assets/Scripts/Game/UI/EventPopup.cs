using System;
using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// Centre-screen popups: national events with choices (the game waits for you) and
    /// simple messages such as the answer to a diplomatic proposal.
    /// </summary>
    public sealed class EventPopup
    {
        readonly GameRoot _game;
        readonly TooltipManager _tips;
        readonly VisualElement _backdrop;
        readonly VisualElement _panel;
        readonly Queue<Action> _queue = new Queue<Action>();

        public VisualElement Root => _backdrop;
        public bool IsOpen { get; private set; }

        public EventPopup(GameRoot game, TooltipManager tips)
        {
            _game = game;
            _tips = tips;
            _backdrop = Ui.Element("gs-backdrop", true);
            _panel = Ui.Panel();
            _panel.style.width = 600;
            _panel.style.maxHeight = Length.Percent(85);
            _backdrop.Add(_panel);
            Ui.Show(_backdrop, false);
        }

        /// <summary>Queues a national event; the player must pick an option.</summary>
        public void ShowEvent(NationalEvent evt)
        {
            Enqueue(() => BuildEvent(evt));
        }

        /// <summary>Queues a message with an OK button and optional reasons.</summary>
        public void ShowMessage(string icon, string title, string text, Breakdown reasons = null, bool good = true)
        {
            Enqueue(() => BuildMessage(icon, title, text, reasons, good));
        }

        void Enqueue(Action build)
        {
            if (IsOpen)
            {
                _queue.Enqueue(build);
                return;
            }
            Open(build);
        }

        void Open(Action build)
        {
            IsOpen = true;
            _panel.Clear();
            build();
            Ui.Show(_backdrop, true);
            _backdrop.BringToFront();
            Ui.Audio?.Play(Audio.Sfx.Notification);
        }

        void Close()
        {
            _tips.Hide();
            if (_queue.Count > 0)
            {
                Open(_queue.Dequeue());
                return;
            }
            IsOpen = false;
            Ui.Show(_backdrop, false);
        }

        VisualElement Header(string icon, string title, Color? iconColor = null)
        {
            var header = Ui.Element("gs-panel__header", true);
            var i = Ui.IconElement(icon ?? "news", 44, iconColor, "gs-icon--large");
            i.style.marginRight = 14;
            header.Add(i);
            var t = Ui.Title(title, 26);
            t.style.flexShrink = 1;
            header.Add(t);
            return header;
        }

        void BuildEvent(NationalEvent evt)
        {
            _panel.Add(Header(evt.Icon, evt.Title));
            var body = Ui.Element("gs-panel__body");
            if (evt.Other != null)
            {
                var who = Ui.Row();
                who.Add(Ui.FlagElement(evt.Other.Tag, "gs-flag--small"));
                var n = Ui.Label(evt.Other.Name, 14, Ui.Gold, Ui.Weight.SemiBold);
                n.style.marginLeft = 8;
                who.Add(n);
                who.style.marginBottom = 6;
                body.Add(who);
            }
            var text = Ui.Label(evt.Text, 16);
            text.style.marginBottom = 10;
            body.Add(text);
            for (int i = 0; i < evt.Options.Count; i++)
            {
                int index = i;
                var option = evt.Options[i];
                var box = Ui.Element("gs-option", true);
                box.Add(Ui.Label(option.Label, 16, null, Ui.Weight.SemiBold));
                if (!string.IsNullOrEmpty(option.Effects))
                    box.Add(Ui.Label(option.Effects, 13, Ui.Gold));
                box.RegisterCallback<ClickEvent>(_ =>
                {
                    Ui.Audio?.Play(Audio.Sfx.UiClick);
                    _game.ResolveEvent(evt, index);
                    Close();
                });
                box.RegisterCallback<PointerEnterEvent>(_ => Ui.Audio?.Play(Audio.Sfx.UiHover));
                body.Add(box);
            }
            _panel.Add(body);
        }

        void BuildMessage(string icon, string title, string text, Breakdown reasons, bool good)
        {
            _panel.Add(Header(icon, title, good ? (Color?)null : Ui.Bad));
            var body = Ui.Element("gs-panel__body");
            body.Add(Ui.Label(text, 16));
            if (reasons != null && reasons.Factors.Count > 0)
            {
                body.Add(Ui.SectionTitle("Their reasons"));
                foreach (var f in reasons.Factors)
                {
                    bool required = Math.Abs(f.Value) < 0.5;
                    body.Add(Ui.StatRow(f.Label, required ? "required" : Ui.FormatSigned(f.Value, "0"),
                        required ? Ui.Warn : f.Value >= 0 ? Ui.Good : Ui.Bad));
                }
                body.Add(Ui.StatRow("Total", Ui.FormatSigned(reasons.Total, "0"), reasons.Total >= 0 ? Ui.Good : Ui.Bad));
            }
            var ok = Ui.Button("OK", Close, true, 16);
            ok.style.marginTop = 14;
            ok.style.alignSelf = Align.FlexEnd;
            ok.style.minWidth = 120;
            body.Add(ok);
            _panel.Add(body);
        }
    }
}
