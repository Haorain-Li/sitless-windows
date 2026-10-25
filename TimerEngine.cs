using System;

namespace StandReminder
{
    public enum TimerPhase { Working, Reminder, Break }
    public enum TimerSignal { None, ReminderDue, BreakCompleted, GapIgnored }

    /// <summary>
    /// UI-independent reminder clock. Supply monotonic tick deltas to Tick and call
    /// SetLocalDate before Tick for calendar-day counters. WorkElapsed estimates
    /// tracked desk time since the last completed break, never sitting posture.
    /// A reminder does not start a break: only StartBreak does. Snoozing and skipping
    /// a partial break retain accrued work. Deltas above 15 seconds are ignored so
    /// suspend/resume or a blocked UI cannot be mistaken for completed movement.
    /// Configure preserves work, clears snooze, and applies a changed break duration
    /// to the NEXT break; an active break keeps the duration it started with.
    /// </summary>
    public sealed class TimerEngine
    {
        private static readonly TimeSpan MaxTick = TimeSpan.FromSeconds(15);
        private TimeSpan workElapsed;
        private TimeSpan breakElapsed;
        private TimeSpan activeBreakDuration;
        private TimeSpan reminderAt;

        public TimerEngine()
            : this(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(3), DateTime.Today) { }

        public TimerEngine(TimeSpan workDuration, TimeSpan breakDuration, DateTime localDate)
        {
            ValidateDuration(workDuration, "workDuration");
            ValidateDuration(breakDuration, "breakDuration");
            WorkDuration = workDuration;
            BreakDuration = breakDuration;
            activeBreakDuration = breakDuration;
            reminderAt = workDuration;
            LocalDate = localDate.Date;
            Phase = TimerPhase.Working;
        }

        public TimerPhase Phase { get; private set; }
        public bool IsPaused { get; private set; }
        public TimeSpan WorkDuration { get; private set; }
        public TimeSpan BreakDuration { get; private set; }
        public TimeSpan ActiveBreakDuration { get { return activeBreakDuration; } }
        public TimeSpan WorkElapsed { get { return workElapsed; } }
        public TimeSpan WorkRemaining { get { return NonNegative(reminderAt - workElapsed); } }
        public TimeSpan BreakElapsed { get { return breakElapsed; } }
        public TimeSpan BreakRemaining { get { return NonNegative(activeBreakDuration - breakElapsed); } }
        public int CompletedBreaksToday { get; private set; }
        public TimeSpan TodayWorkElapsed { get; private set; }
        public DateTime LocalDate { get; private set; }

        public TimerSignal Tick(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("elapsed");
            if (elapsed > MaxTick)
                return TimerSignal.GapIgnored;
            if (IsPaused || elapsed == TimeSpan.Zero)
                return TimerSignal.None;

            if (Phase == TimerPhase.Break)
            {
                breakElapsed += elapsed;
                if (breakElapsed >= activeBreakDuration)
                {
                    CompletedBreaksToday++;
                    ResetCurrentSession();
                    return TimerSignal.BreakCompleted;
                }
                return TimerSignal.None;
            }

            workElapsed += elapsed;
            TodayWorkElapsed += elapsed;
            if (Phase == TimerPhase.Working && workElapsed >= reminderAt)
            {
                Phase = TimerPhase.Reminder;
                return TimerSignal.ReminderDue;
            }
            return TimerSignal.None;
        }

        public void Pause() { IsPaused = true; }
        public void Resume() { IsPaused = false; }
        public void TogglePause() { IsPaused = !IsPaused; }

        public void StartBreak()
        {
            if (Phase != TimerPhase.Break)
            {
                activeBreakDuration = BreakDuration;
                breakElapsed = TimeSpan.Zero;
                Phase = TimerPhase.Break;
            }
            IsPaused = false;
        }

        public void Snooze(TimeSpan duration)
        {
            ValidateDuration(duration, "duration");
            if (Phase != TimerPhase.Reminder)
                return;
            reminderAt = workElapsed + duration;
            Phase = TimerPhase.Working;
            IsPaused = false;
        }

        public void SkipBreak()
        {
            if (Phase != TimerPhase.Break)
                return;
            breakElapsed = TimeSpan.Zero;
            Phase = workElapsed >= reminderAt ? TimerPhase.Reminder : TimerPhase.Working;
            IsPaused = false;
        }

        public void ResetSession()
        {
            ResetCurrentSession();
            IsPaused = false;
        }

        public void Configure(TimeSpan workDuration, TimeSpan breakDuration)
        {
            ValidateDuration(workDuration, "workDuration");
            ValidateDuration(breakDuration, "breakDuration");
            WorkDuration = workDuration;
            BreakDuration = breakDuration;
            reminderAt = WorkDuration;
            if (Phase != TimerPhase.Break)
            {
                activeBreakDuration = BreakDuration;
                Phase = workElapsed >= reminderAt ? TimerPhase.Reminder : TimerPhase.Working;
            }
        }

        public void SetLocalDate(DateTime localDate)
        {
            if (localDate.Date == LocalDate)
                return;
            LocalDate = localDate.Date;
            CompletedBreaksToday = 0;
            TodayWorkElapsed = TimeSpan.Zero;
        }

        /// <summary>Restore only calendar-day counters; a restarted app begins a fresh session.</summary>
        public void RestoreDailyStats(DateTime date, TimeSpan todayWork, int completed)
        {
            if (date.Date != LocalDate)
                return;
            if (todayWork < TimeSpan.Zero || todayWork > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException("todayWork");
            if (completed < 0 || completed > 100000)
                throw new ArgumentOutOfRangeException("completed");
            TodayWorkElapsed = todayWork;
            CompletedBreaksToday = completed;
        }

        private void ResetCurrentSession()
        {
            workElapsed = TimeSpan.Zero;
            breakElapsed = TimeSpan.Zero;
            activeBreakDuration = BreakDuration;
            reminderAt = WorkDuration;
            Phase = TimerPhase.Working;
        }

        private static void ValidateDuration(TimeSpan duration, string name)
        {
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(name, "Duration must be greater than zero and no more than one day.");
        }

        private static TimeSpan NonNegative(TimeSpan duration)
        {
            return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        }
    }
}
