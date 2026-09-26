using System;
using System.Collections.Generic;
using System.Linq;

namespace GrandStrategy.Simulation
{
    public enum DiplomaticAction
    {
        ImproveRelations,
        Denounce,
        ProposeTrade,
        CancelTrade,
        ImposeSanctions,
        LiftSanctions,
        ProposePact,
        JoinAlliance,
        LeaveAlliance,
        DeclareWar,
    }

    /// <summary>What would happen if a country took an action: cost, availability, and the other side's answer.</summary>
    public sealed class ActionPreview
    {
        public DiplomaticAction Action;
        public bool Available;
        public string Reason = "";          // why it's not available
        public string Title = "";
        public string Description = "";
        public double CostMillions;
        public bool NeedsConsent;           // the other side must accept
        public bool WillAccept;
        public Breakdown Opinion = new Breakdown(); // reasons behind their answer
        public Bloc Alliance;               // for join / leave
    }

    public sealed class ActionResult
    {
        public bool Done;
        public bool Accepted;
        public string Message = "";
        public ActionPreview Preview;
    }

    /// <summary>
    /// Relations drift, diplomatic actions, AI answers to proposals and light AI initiative.
    /// </summary>
    public sealed class DiplomacySystem
    {
        readonly GameSimulation _sim;
        readonly List<(Country by, Country target)> _pendingRetaliation = new List<(Country, Country)>();

        internal DiplomacySystem(GameSimulation sim) => _sim = sim;

        DiplomacyRules R => _sim.DiplomacyRules;
        DiplomacyState D => _sim.Diplomacy;
        IReadOnlyList<Country> All => _sim.World.Countries;

        internal void Initialize()
        {
            var index = new Dictionary<string, int>();
            foreach (var c in All)
                index[c.Tag] = c.Index;
            D.SetIndex(index);
            D.MarkBlocsChanged();
            for (int a = 0; a < All.Count; a++)
                for (int b = a + 1; b < All.Count; b++)
                    D.SetRelations(a, b, Target(All[a], All[b]));
        }

        // ------------------------------------------------------------------ relations

        /// <summary>Where relations between two countries are heading, with reasons.</summary>
        public Breakdown RelationsTarget(Country a, Country b)
        {
            var r = new Breakdown();
            if (D.TryGetOverride(a.Index, b.Index, out double fixedValue))
            {
                r.Add("History between us", fixedValue);
            }
            else
            {
                r.Add("Shared alliances and blocs", D.MembershipBonus(a.Index, b.Index, R));

                var ga = a.Politics?.Government ?? GovernmentType.FlawedDemocracy;
                var gb = b.Politics?.Government ?? GovernmentType.FlawedDemocracy;
                if (ga.IsDemocracy() && gb.IsDemocracy()) r.Add("Both democracies", R.democraciesBonus);
                else if ((ga.IsDemocracy() && gb.IsAutocracy()) || (ga.IsAutocracy() && gb.IsDemocracy())) r.Add("Democracy vs. autocracy", -R.regimeClashPenalty);
                else if (ga.IsAutocracy() && gb.IsAutocracy()) r.Add("Fellow autocracies", R.autocraciesBonus);
            }

            // Sanctions that existed at the start are already part of hand-set history.
            bool overridden = D.TryGetOverride(a.Index, b.Index, out _);
            int sanctions = 0;
            if (D.HasSanctions(a.Index, b.Index) && (!overridden || !D.IsStartSanction(a.Index, b.Index))) sanctions++;
            if (D.HasSanctions(b.Index, a.Index) && (!overridden || !D.IsStartSanction(b.Index, a.Index))) sanctions++;
            if (sanctions > 0)
                r.Add("Sanctions", -R.sanctionsPenalty * sanctions);
            if (D.HasTradeDeal(a, b))
                r.Add("Trade agreement", R.tradeAgreementBonus);
            r.Add("Recent diplomacy", D.Goodwill(a, b));
            return r;
        }

        /// <summary>Same as <see cref="RelationsTarget"/> without the breakdown (fast, used every month).</summary>
        double Target(Country a, Country b)
        {
            double t;
            bool overridden = D.TryGetOverride(a.Index, b.Index, out double fixedValue);
            if (overridden)
            {
                t = fixedValue;
            }
            else
            {
                t = D.MembershipBonus(a.Index, b.Index, R);
                var ga = a.Politics?.Government ?? GovernmentType.FlawedDemocracy;
                var gb = b.Politics?.Government ?? GovernmentType.FlawedDemocracy;
                if (ga.IsDemocracy() && gb.IsDemocracy()) t += R.democraciesBonus;
                else if ((ga.IsDemocracy() && gb.IsAutocracy()) || (ga.IsAutocracy() && gb.IsDemocracy())) t -= R.regimeClashPenalty;
                else if (ga.IsAutocracy() && gb.IsAutocracy()) t += R.autocraciesBonus;
            }
            if (D.HasSanctions(a.Index, b.Index) && (!overridden || !D.IsStartSanction(a.Index, b.Index))) t -= R.sanctionsPenalty;
            if (D.HasSanctions(b.Index, a.Index) && (!overridden || !D.IsStartSanction(b.Index, a.Index))) t -= R.sanctionsPenalty;
            if (D.HasTradeDeal(a, b)) t += R.tradeAgreementBonus;
            t += D.Goodwill(a, b);
            return Math.Max(-100, Math.Min(100, t));
        }

        internal void Monthly(GameDate date)
        {
            D.DecayGoodwill(R.goodwillDecay);
            for (int a = 0; a < All.Count; a++)
            {
                if (All[a].IsEliminated)
                    continue;
                for (int b = a + 1; b < All.Count; b++)
                {
                    if (All[b].IsEliminated)
                        continue;
                    double current = D.RawRelations(a, b);
                    D.SetRelations(a, b, current + (Target(All[a], All[b]) - current) * R.driftRate);
                }
            }
            RunRetaliation();
            AiInitiative();
        }

        // ------------------------------------------------------------------ previews

        public ActionPreview Preview(Country from, Country to, DiplomaticAction action)
        {
            var p = new ActionPreview { Action = action, Available = true };
            if (from == null || to == null || from == to || from.IsEliminated || to.IsEliminated)
                return Unavailable(p, "Not possible.");
            var today = _sim.Date;

            switch (action)
            {
                case DiplomaticAction.ImproveRelations:
                    p.Title = "Improve relations";
                    p.CostMillions = Math.Max(R.improveMinCost, _sim.GdpOf(from) * R.improveCostShare);
                    p.Description = $"Send a diplomatic mission. Relations +{R.improveImmediate:0} now and up to +{R.improveGoodwill:0} over time.";
                    if (D.OnCooldown(from.Index, to.Index, action, today, out var ready))
                        return Unavailable(p, $"Our last mission was recent. Possible again on {ready}.");
                    if (from.Economy.Treasury < p.CostMillions)
                        return Unavailable(p, "Not enough money in the treasury.");
                    break;

                case DiplomaticAction.Denounce:
                    p.Title = "Denounce";
                    p.Description = "Publicly condemn their government. Relations fall sharply; hawks at home approve.";
                    if (D.OnCooldown(from.Index, to.Index, action, today, out var ready2))
                        return Unavailable(p, $"We denounced them recently. Possible again on {ready2}.");
                    break;

                case DiplomaticAction.ProposeTrade:
                    p.Title = "Propose trade agreement";
                    p.Description = "Lower trade barriers. Both economies grow faster; relations improve.";
                    p.NeedsConsent = true;
                    if (D.AreTradePartners(from, to))
                        return Unavailable(p, "We already trade freely.");
                    if (D.AnySanctions(from, to))
                        return Unavailable(p, "Sanctions are in force between us.");
                    EvaluateTrade(from, to, p);
                    break;

                case DiplomaticAction.CancelTrade:
                    p.Title = "Cancel trade agreement";
                    p.Description = "End our bilateral trade agreement. Growth and relations suffer.";
                    if (!D.HasTradeDeal(from, to))
                        return Unavailable(p, D.AreTradePartners(from, to)
                            ? "We trade through a shared bloc, not a bilateral deal."
                            : "We have no trade agreement.");
                    break;

                case DiplomaticAction.ImposeSanctions:
                    p.Title = "Impose sanctions";
                    p.Description = "Restrict trade and finance. Their growth falls (more if we are a big economy); ours falls a little too.";
                    if (D.Sanctions(from, to))
                        return Unavailable(p, "We already sanction them.");
                    break;

                case DiplomaticAction.LiftSanctions:
                    p.Title = "Lift sanctions";
                    p.Description = "End our sanctions. Relations recover and trade resumes.";
                    if (!D.Sanctions(from, to))
                        return Unavailable(p, "We don't sanction them.");
                    break;

                case DiplomaticAction.ProposePact:
                    p.Title = "Propose defence pact";
                    p.Description = "A mutual defence alliance: each side defends the other if attacked (from Phase 2).";
                    p.NeedsConsent = true;
                    if (D.AreAllied(from, to))
                        return Unavailable(p, "We are already allies.");
                    EvaluatePact(from, to, p);
                    break;

                case DiplomaticAction.JoinAlliance:
                    p.Alliance = D.BlocsOf(to).Where(b => b.IsAlliance && !b.Members.Contains(from.Tag))
                        .OrderByDescending(b => b.Members.Count).FirstOrDefault();
                    p.Title = p.Alliance == null ? "Apply to join alliance" : $"Apply to join {p.Alliance.Name}";
                    p.Description = "Ask to become a member. Every member must be willing to defend us.";
                    p.NeedsConsent = true;
                    if (p.Alliance == null || p.Alliance.Members.Count < 3)
                        return Unavailable(p, "They are not in a multi-country alliance we could join.");
                    EvaluateJoin(from, p.Alliance, p);
                    break;

                case DiplomaticAction.LeaveAlliance:
                    p.Alliance = D.SharedBlocs(from, to).FirstOrDefault(b => b.IsAlliance);
                    p.Title = p.Alliance == null ? "Leave alliance" : $"Leave {p.Alliance.Name}";
                    p.Description = "Withdraw from our shared alliance. Former allies will resent it.";
                    if (p.Alliance == null)
                        return Unavailable(p, "We are not allied with them.");
                    break;

                case DiplomaticAction.DeclareWar:
                    p.Title = "Declare war";
                    p.Description = "Armies, fronts and combat.";
                    return Unavailable(p, "Military and war arrive in Phase 2.");
            }
            return p;
        }

        static ActionPreview Unavailable(ActionPreview p, string reason)
        {
            p.Available = false;
            p.Reason = reason;
            return p;
        }

        void EvaluateTrade(Country from, Country to, ActionPreview p)
        {
            var o = p.Opinion;
            o.Add("Our relations", D.Relations(to, from) * 0.6);
            if (D.SharedBlocs(from, to).Any())
                o.Add("We share a bloc", 10);
            double ratio = _sim.GdpOf(from) / Math.Max(1, _sim.GdpOf(to));
            if (ratio > 1.5)
                o.Add("Access to a bigger market", Math.Min(15, 5 * Math.Log(ratio, 2)));
            else if (ratio < 0.2)
                o.Add("Their market is small for us", -5);
            AddFriendsOfEnemies(from, to, o);
            p.WillAccept = o.Total >= R.tradeAcceptScore;
            o.Add("Needed to accept", -R.tradeAcceptScore);
        }

        void EvaluatePact(Country from, Country to, ActionPreview p)
        {
            var o = p.Opinion;
            double relations = D.Relations(to, from);
            o.Add("Our relations", relations);
            o.Add("An alliance is a big commitment", -30);
            if (SharesRival(from, to))
                o.Add("We share a rival", 15);
            var ga = from.Politics.Government;
            var gb = to.Politics.Government;
            if ((ga.IsDemocracy() && gb.IsDemocracy()) || (ga.IsAutocracy() && gb.IsAutocracy()))
                o.Add("Similar governments", 10);
            else if ((ga.IsDemocracy() && gb.IsAutocracy()) || (ga.IsAutocracy() && gb.IsDemocracy()))
                o.Add("Very different governments", -20);
            foreach (var bloc in D.BlocsOf(to).Where(b => b.IsAlliance))
            {
                if (bloc.Members.Any(m => m != to.Tag && D.Relations(_sim.World.GetCountry(m), from) < -40))
                {
                    o.Add($"Our {bloc.Name} allies distrust you", -40);
                    break;
                }
            }
            p.WillAccept = o.Total >= R.pactAcceptScore && relations >= R.pactMinRelations;
            o.Add("Needed to accept", -R.pactAcceptScore);
            if (relations < R.pactMinRelations)
                o.Add($"Relations must be at least {R.pactMinRelations:0}", 0.001);
        }

        void EvaluateJoin(Country from, Bloc alliance, ActionPreview p)
        {
            var o = p.Opinion;
            var members = alliance.Members.Select(m => _sim.World.GetCountry(m)).Where(m => m != null && !m.IsEliminated).ToList();
            double avg = members.Average(m => D.Relations(m, from));
            double min = members.Min(m => D.Relations(m, from));
            o.Add("Average relations with members", avg);
            o.Add("Membership bar", -R.joinMinAverage);
            if (min < 0)
            {
                var worst = members.OrderBy(m => D.Relations(m, from)).First();
                o.Add($"{worst.Name} opposes us", min);
            }
            int democracies = members.Count(m => m.Politics.Government.IsDemocracy());
            bool democraticClub = democracies * 2 > members.Count;
            if (democraticClub && !from.Politics.Government.IsDemocracy())
                o.Add("Members want a democracy", -15);
            p.WillAccept = o.Total >= 0;
        }

        void AddFriendsOfEnemies(Country from, Country to, Breakdown o)
        {
            foreach (var friend in All)
            {
                if (friend == from || friend == to || friend.IsEliminated)
                    continue;
                if (D.Relations(to, friend) > 60 && D.Relations(friend, from) < -40)
                {
                    o.Add($"Their friend {friend.Name} is hostile to us", -15);
                    return;
                }
            }
        }

        bool SharesRival(Country a, Country b)
        {
            foreach (var third in All)
                if (third != a && third != b && !third.IsEliminated && D.Relations(a, third) < -40 && D.Relations(b, third) < -40)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ execution

        public ActionResult Execute(Country from, Country to, DiplomaticAction action)
        {
            var preview = Preview(from, to, action);
            var result = new ActionResult { Preview = preview };
            if (!preview.Available)
            {
                result.Message = preview.Reason;
                return result;
            }
            result.Done = true;
            var today = _sim.Date;

            switch (action)
            {
                case DiplomaticAction.ImproveRelations:
                    _sim.Economy.Spend(from, preview.CostMillions);
                    D.AddGoodwill(from.Index, to.Index, R.improveGoodwill);
                    D.SetRelations(from.Index, to.Index, D.Relations(from, to) + R.improveImmediate);
                    D.StartCooldown(from.Index, to.Index, action, today.AddDays(30 * R.improveCooldownMonths));
                    result.Message = $"Our diplomats are working to improve relations with {to.Name}.";
                    break;

                case DiplomaticAction.Denounce:
                    D.AddGoodwill(from.Index, to.Index, R.denounceGoodwill);
                    D.SetRelations(from.Index, to.Index, D.Relations(from, to) + R.denounceImmediate);
                    D.StartCooldown(from.Index, to.Index, action, today.AddDays(180));
                    if (D.Relations(from, to) < -30)
                        from.AddOrRefreshModifier(new Modifier("rally_denounce", "Standing up to rivals", "Hawks applaud our tough stance.", 0, 90) { Approval = 2 });
                    result.Message = $"We denounced the government of {to.Name}.";
                    _sim.News(_sim.ImportanceOf(from, to), $"{from.Name} denounces {to.Name}", "A sharp diplomatic rebuke.", from, to);
                    break;

                case DiplomaticAction.ProposeTrade:
                    result.Accepted = preview.WillAccept;
                    if (result.Accepted)
                    {
                        SignTrade(from, to);
                        result.Message = $"{to.Name} accepted. The trade agreement is signed.";
                    }
                    else
                    {
                        result.Message = $"{to.Name} declined our trade proposal.";
                    }
                    break;

                case DiplomaticAction.CancelTrade:
                    D.RemoveTradeDeal(from, to);
                    D.AddGoodwill(from.Index, to.Index, -15);
                    result.Message = $"We cancelled our trade agreement with {to.Name}.";
                    _sim.News(_sim.ImportanceOf(from, to), $"{from.Name} ends trade deal with {to.Name}", "", from, to);
                    break;

                case DiplomaticAction.ImposeSanctions:
                    ImposeSanctions(from, to);
                    result.Message = $"We imposed sanctions on {to.Name}.";
                    if (_sim.GdpShare(to) >= 0.01 && _sim.Rng.NextDouble() < R.aiRetaliationChance && !_sim.IsPlayer(to))
                        _pendingRetaliation.Add((to, from));
                    break;

                case DiplomaticAction.LiftSanctions:
                    D.RemoveSanctions(from, to);
                    D.AddGoodwill(from.Index, to.Index, 10);
                    result.Message = $"We lifted our sanctions on {to.Name}.";
                    _sim.News(_sim.ImportanceOf(from, to), $"{from.Name} lifts sanctions on {to.Name}", "", from, to);
                    break;

                case DiplomaticAction.ProposePact:
                    result.Accepted = preview.WillAccept;
                    if (result.Accepted)
                    {
                        var pact = new Bloc($"PACT-{from.Tag}-{to.Tag}", $"{from.Name}-{to.Name} Pact", BlocKind.Alliance, new[] { from.Tag, to.Tag });
                        D.Blocs.Add(pact);
                        D.MarkBlocsChanged();
                        result.Message = $"{to.Name} agreed. We are now allies.";
                        _sim.News(_sim.ImportanceOf(from, to), $"{from.Name} and {to.Name} form an alliance", "A mutual defence pact was signed.", from, to);
                    }
                    else
                    {
                        result.Message = $"{to.Name} refused an alliance.";
                    }
                    break;

                case DiplomaticAction.JoinAlliance:
                    result.Accepted = preview.WillAccept;
                    if (result.Accepted)
                    {
                        preview.Alliance.Members.Add(from.Tag);
                        D.MarkBlocsChanged();
                        result.Message = $"We joined {preview.Alliance.Name}.";
                        _sim.News(NewsImportance.Notable, $"{from.Name} joins {preview.Alliance.Name}", "", from, to);
                    }
                    else
                    {
                        result.Message = $"{preview.Alliance.Name} rejected our application.";
                    }
                    break;

                case DiplomaticAction.LeaveAlliance:
                    preview.Alliance.Members.Remove(from.Tag);
                    foreach (var tag in preview.Alliance.Members)
                    {
                        var member = _sim.World.GetCountry(tag);
                        if (member != null)
                            D.AddGoodwill(from.Index, member.Index, -20);
                    }
                    if (preview.Alliance.Members.Count < 2)
                        D.Blocs.Remove(preview.Alliance);
                    D.MarkBlocsChanged();
                    result.Message = $"We left {preview.Alliance.Name}.";
                    _sim.News(NewsImportance.Notable, $"{from.Name} leaves {preview.Alliance.Name}", "", from, to);
                    break;
            }

            _sim.Economy.ComputeGrowth(from);
            _sim.Economy.ComputeGrowth(to);
            _sim.World.Events.Publish(new DiplomacyChanged(from.Tag, to.Tag));
            return result;
        }

        internal void SignTrade(Country a, Country b)
        {
            D.AddTradeDeal(a, b);
            D.AddGoodwill(a.Index, b.Index, 5);
            _sim.News(_sim.ImportanceOf(a, b), $"{a.Name} and {b.Name} sign a trade agreement", "Both economies should benefit.", a, b);
            _sim.World.Events.Publish(new DiplomacyChanged(a.Tag, b.Tag));
        }

        internal void ImposeSanctions(Country by, Country target)
        {
            D.AddSanctions(by, target);
            D.RemoveTradeDeal(by, target);
            D.AddGoodwill(by.Index, target.Index, -20);
            _sim.News(_sim.ImportanceOf(by, target), $"{by.Name} sanctions {target.Name}", "Trade and finance are restricted.", by, target);
            if (_sim.IsPlayer(target))
                _sim.Events.RaiseSanctioned(target, by);
            _sim.World.Events.Publish(new DiplomacyChanged(by.Tag, target.Tag));
        }

        // ------------------------------------------------------------------ AI

        void RunRetaliation()
        {
            foreach (var (by, target) in _pendingRetaliation)
                if (!by.IsEliminated && !target.IsEliminated && !D.Sanctions(by, target))
                    ImposeSanctions(by, target);
            _pendingRetaliation.Clear();
        }

        void AiInitiative()
        {
            var rng = _sim.Rng;
            var player = _sim.World.Player;

            // Friendly countries sometimes offer the player a trade deal.
            if (player != null && !player.IsEliminated && rng.NextDouble() < R.aiTradeOfferChance)
            {
                var candidates = All.Where(c => c != player && !c.IsEliminated && D.Relations(c, player) >= 35
                                                && !D.AreTradePartners(c, player) && !D.AnySanctions(c, player)).ToList();
                if (candidates.Count > 0)
                    _sim.Events.RaiseTradeOffer(player, candidates[rng.Next(candidates.Count)]);
            }

            // AI countries trade with friends...
            for (int i = 0; i < 3; i++)
            {
                var a = All[rng.Next(All.Count)];
                if (a.IsEliminated || _sim.IsPlayer(a))
                    continue;
                var partner = All.Where(b => b != a && !b.IsEliminated && !_sim.IsPlayer(b) && D.Relations(a, b) >= 40
                                             && !D.AreTradePartners(a, b) && !D.AnySanctions(a, b))
                                 .OrderByDescending(b => D.Relations(a, b)).FirstOrDefault();
                if (partner != null)
                    SignTrade(a, partner);
            }

            // ...and sanction bitter enemies.
            foreach (var by in All)
            {
                if (by.IsEliminated || _sim.IsPlayer(by) || _sim.GdpShare(by) < 0.005)
                    continue;
                foreach (var target in All)
                {
                    if (target == by || target.IsEliminated || D.Sanctions(by, target) || D.Relations(by, target) > -80)
                        continue;
                    if (rng.NextDouble() < R.aiSanctionChance)
                        ImposeSanctions(by, target);
                }
            }
        }
    }
}
