using System;
using StandReminder;

internal static class TimerEngineTests
{
    private static int assertions;
    private static readonly DateTime Day = new DateTime(2026, 10, 25);

    private static TimerEngine Clock(double work, double rest)
    {
        return new TimerEngine(TimeSpan.FromSeconds(work), TimeSpan.FromSeconds(rest), Day);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Object.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }

    private static void Seconds(double expected, TimeSpan actual, string message)
    {
        Equal(expected, actual.TotalSeconds, message);
    }

    private static void Throws(Action action, string message)
    {
        assertions++;
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception(message + ": expected ArgumentOutOfRangeException");
    }

    public static int Main()
    {
        try
        {
            var clock = Clock(10, 3);
            Equal(TimerPhase.Working, clock.Phase, "Starts working");
            Equal(TimerSignal.None, clock.Tick(TimeSpan.FromSeconds(9)), "Not due early");
            Seconds(1, clock.WorkRemaining, "Countdown");
            Equal(TimerSignal.ReminderDue, clock.Tick(TimeSpan.FromSeconds(1)), "Due at boundary");
            Equal(TimerPhase.Reminder, clock.Phase, "Reminder waits for user");
            Equal(TimerSignal.None, clock.Tick(TimeSpan.FromSeconds(5)), "Reminder emitted once");
            Seconds(15, clock.WorkElapsed, "Overdue work continues");
            Equal(0, clock.CompletedBreaksToday, "No automatic break credit");

            clock.Snooze(TimeSpan.FromSeconds(5));
            Seconds(5, clock.WorkRemaining, "Snooze from current time");
            Seconds(15, clock.WorkElapsed, "Snooze preserves accrued work");
            clock.Tick(TimeSpan.FromSeconds(5));
            Equal(TimerPhase.Reminder, clock.Phase, "Snooze expires");
            clock.StartBreak();
            clock.Tick(TimeSpan.FromSeconds(2));
            clock.SkipBreak();
            Equal(0, clock.CompletedBreaksToday, "Partial break earns no credit");
            Seconds(20, clock.WorkElapsed, "Skipping preserves work");
            Equal(TimerPhase.Reminder, clock.Phase, "Skipped overdue break remains due");
            clock.StartBreak();
            Equal(TimerSignal.BreakCompleted, clock.Tick(TimeSpan.FromSeconds(3)), "Full break completes");
            Equal(1, clock.CompletedBreaksToday, "Full break credited");
            Seconds(0, clock.WorkElapsed, "Full break resets session");
            Seconds(20, clock.TodayWorkElapsed, "Break never counted as work");
            Seconds(10, clock.WorkRemaining, "New countdown");

            clock.Tick(TimeSpan.FromSeconds(2));
            clock.Pause();
            clock.Tick(TimeSpan.FromSeconds(8));
            Seconds(2, clock.WorkElapsed, "Pause freezes work");
            clock.Resume();
            clock.Tick(TimeSpan.FromSeconds(1));
            Seconds(3, clock.WorkElapsed, "Resume retains progress");
            clock.TogglePause();
            Equal(true, clock.IsPaused, "Toggle pauses");
            clock.StartBreak();
            Equal(false, clock.IsPaused, "Starting break resumes explicit timer");
            clock.Tick(TimeSpan.FromSeconds(1));
            clock.Pause();
            clock.Tick(TimeSpan.FromSeconds(5));
            Seconds(2, clock.BreakRemaining, "Pause freezes break");
            clock.Resume();
            clock.SkipBreak();
            Equal(TimerPhase.Working, clock.Phase, "Skipped early break retains work phase");
            Seconds(3, clock.WorkElapsed, "Early break skip retains work");

            Equal(TimerSignal.GapIgnored, clock.Tick(TimeSpan.FromHours(8)), "Sleep gap ignored");
            Seconds(3, clock.WorkElapsed, "Sleep cannot add work");
            clock.StartBreak();
            clock.Tick(TimeSpan.FromSeconds(1));
            Equal(TimerSignal.GapIgnored, clock.Tick(TimeSpan.FromMinutes(10)), "Sleep during break ignored");
            Seconds(2, clock.BreakRemaining, "Sleep cannot complete break");
            Equal(1, clock.CompletedBreaksToday, "Sleep earns no completion");

            clock.Configure(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
            Seconds(2, clock.BreakRemaining, "Changing settings preserves active break duration");
            clock.Tick(TimeSpan.FromSeconds(2));
            Equal(2, clock.CompletedBreaksToday, "Original active break completes normally");
            clock.StartBreak();
            Seconds(1, clock.BreakRemaining, "New break uses new duration");
            clock.SkipBreak();
            clock.Tick(TimeSpan.FromSeconds(2));
            Equal(TimerPhase.Reminder, clock.Phase, "Changed work duration used");
            clock.Configure(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3));
            Equal(TimerPhase.Working, clock.Phase, "Lengthened interval returns to work");
            Seconds(18, clock.WorkRemaining, "Settings preserve work");
            clock.Snooze(TimeSpan.FromSeconds(5));
            Seconds(18, clock.WorkRemaining, "Snooze ignored before due");
            clock.Configure(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
            Equal(TimerPhase.Reminder, clock.Phase, "Shortened interval immediately due");

            clock.SetLocalDate(Day);
            Equal(2, clock.CompletedBreaksToday, "Same date preserves totals");
            clock.SetLocalDate(Day.AddDays(1).AddHours(8));
            Equal(0, clock.CompletedBreaksToday, "New local day resets completions");
            Seconds(0, clock.TodayWorkElapsed, "New day resets tracked total");
            Seconds(2, clock.WorkElapsed, "Midnight preserves current session");
            Equal(Day.AddDays(1), clock.LocalDate, "Date normalized");
            clock.Tick(TimeSpan.FromSeconds(1));
            Seconds(1, clock.TodayWorkElapsed, "New day's work accrues");
            clock.ResetSession();
            Equal(0, clock.CompletedBreaksToday, "Manual reset earns no credit");
            Seconds(0, clock.WorkElapsed, "Manual reset clears current work");
            Seconds(1, clock.TodayWorkElapsed, "Manual reset retains daily total");

            clock.RestoreDailyStats(Day, TimeSpan.FromSeconds(120), 8);
            Seconds(1, clock.TodayWorkElapsed, "Old persisted date ignored");
            clock.RestoreDailyStats(Day.AddDays(1), TimeSpan.FromSeconds(120), 8);
            Seconds(120, clock.TodayWorkElapsed, "Same-day work restored");
            Equal(8, clock.CompletedBreaksToday, "Same-day breaks restored");
            Seconds(0, clock.WorkElapsed, "Restore does not restore a sitting session");
            Throws(delegate { clock.RestoreDailyStats(Day.AddDays(1), TimeSpan.FromSeconds(-1), 1); }, "Negative saved work rejected");
            Throws(delegate { clock.RestoreDailyStats(Day.AddDays(1), TimeSpan.FromSeconds(1), -1); }, "Negative saved breaks rejected");
            Throws(delegate { clock.RestoreDailyStats(Day.AddDays(1), TimeSpan.FromSeconds(1), Int32.MaxValue); }, "Implausible saved breaks rejected");
            Equal(8, clock.CompletedBreaksToday, "Rejected restore atomic");

            clock = Clock(30, 3);
            clock.Tick(TimeSpan.FromSeconds(15));
            Seconds(15, clock.WorkElapsed, "15 second boundary accepted");
            Equal(TimerSignal.GapIgnored, clock.Tick(TimeSpan.FromMilliseconds(15001)), "Above boundary ignored");
            Equal(TimerSignal.None, clock.Tick(TimeSpan.Zero), "Zero delta harmless");
            Throws(delegate { clock.Tick(TimeSpan.FromTicks(-1)); }, "Negative delta rejected");
            Throws(delegate { Clock(0, 3); }, "Zero work duration rejected");
            Throws(delegate { Clock(10, -1); }, "Negative break duration rejected");
            Throws(delegate { clock.Configure(TimeSpan.FromDays(2), TimeSpan.FromMinutes(3)); }, "Excessive duration rejected");
            Seconds(30, clock.WorkDuration, "Rejected settings are atomic");
            Throws(delegate { clock.Snooze(TimeSpan.Zero); }, "Zero snooze rejected");

            clock = Clock(10, 3);
            clock.Tick(TimeSpan.FromSeconds(10));
            clock.Snooze(TimeSpan.FromSeconds(5));
            clock.Tick(TimeSpan.FromSeconds(1));
            clock.Configure(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3));
            Equal(TimerPhase.Working, clock.Phase, "Saving unchanged settings preserves snooze");
            Seconds(4, clock.WorkRemaining, "Unchanged settings retain snooze deadline");
            clock.Configure(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
            Seconds(4, clock.WorkRemaining, "Changing only break duration preserves snooze");
            clock.Pause();
            clock.Configure(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
            Equal(true, clock.IsPaused, "Saving settings preserves manual pause");
            clock.Resume();
            Equal(TimerSignal.ReminderDue, clock.Tick(TimeSpan.FromSeconds(4)), "Preserved snooze expires normally");
            clock.StartBreak();
            Seconds(7, clock.BreakRemaining, "Updated break duration applies to next break");

            Console.WriteLine("PASS: " + assertions + " timer assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }
}
