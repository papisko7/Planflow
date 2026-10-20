using PlanFlow.Application.Calendar.Common;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Entities;
using PlanFlow.Domain.Enums;
using PlanFlow.Domain.Services;
using Xunit;
using TaskStatus = PlanFlow.Domain.Enums.TaskStatus;

namespace PlanFlow.Tests.Application.Calendar;

/// <summary>The way back to Google: noting an edit of an imported item, deciding who wins, and building the change Google receives.</summary>
public class GoogleMirrorTests
{
    private static readonly DateTime Start = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Edited = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CalendarItem ImportedEvent(Action<CalendarItem>? tweak = null)
    {
        var item = new CalendarItem
        {
            Title = "Standup", TaskType = TaskType.Event, EventTemplate = EventTemplate.Default, StartUtc = Start, EndUtc = Start.AddHours(1),
            IsBusy = true, ExternalCalendarEventId = "e1"
        };
        tweak?.Invoke(item);
        return item;
    }

    private static CalendarItem ImportedTask(Action<CalendarItem>? tweak = null)
    {
        var item = new CalendarItem
        {
            Title = "Write chapter", TaskType = TaskType.Task, Status = TaskStatus.Backlog, DeadlineUtc = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            ExternalTaskId = "t1"
        };
        tweak?.Invoke(item);
        return item;
    }

    private static GoogleCalendarEvent InGoogle(Func<GoogleCalendarEvent, GoogleCalendarEvent>? tweak = null)
    {
        var google = new GoogleCalendarEvent("e1", "Standup", Start, Start.AddHours(1), IsCancelled: false, ReminderMinutes: []);
        return tweak is null ? google : tweak(google);
    }

    private static GoogleTask TaskInGoogle(Func<GoogleTask, GoogleTask>? tweak = null)
    {
        var google = new GoogleTask("t1", "Write chapter", null, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), IsCompleted: false, IsDeleted: false);
        return tweak is null ? google : tweak(google);
    }

    // ---- what changed in an edit ----

    [Fact]
    public void Diff_NamesOnlyTheFieldsThatChanged()
    {
        var cases = new (string Name, Action<CalendarItem> Edit, MirroredFields Expected)[]
        {
            ("title", i => i.Title = "Daily", MirroredFields.Title),
            ("description", i => i.Description = "Notes", MirroredFields.Description),
            ("start", i => i.StartUtc = Start.AddHours(1), MirroredFields.Time),
            ("end", i => i.EndUtc = Start.AddHours(2), MirroredFields.Time),
            ("all-day", i => i.IsAllDay = true, MirroredFields.Time),
            ("location", i => i.Location = "Room 7", MirroredFields.Location),
            ("colour", i => i.ColorKey = "tomato", MirroredFields.Color),
            ("busy", i => i.IsBusy = false, MirroredFields.Busy),
            ("title and place", i => { i.Title = "Daily"; i.Location = "Room 7"; }, MirroredFields.Title | MirroredFields.Location),
            ("impact is not a Google field", i => i.ImpactScore = 9, MirroredFields.None),
            ("effort is not a Google field", i => i.EstimatedEffortHours = 3, MirroredFields.None),
            ("blank description is no description", i => i.Description = "  ", MirroredFields.None)
        };

        foreach (var (name, edit, expected) in cases)
        {
            var item = ImportedEvent();
            var before = GoogleMirror.Capture(item);
            edit(item);

            Assert.True(expected == GoogleMirror.Diff(before, GoogleMirror.Capture(item), isGoogleTask: false),
                $"{name}: expected {expected}, got {GoogleMirror.Diff(before, GoogleMirror.Capture(item), isGoogleTask: false)}");
        }
    }

    [Fact]
    public void Diff_Reminders_AreComparedAsASet_WhateverTheirOrder()
    {
        var item = ImportedEvent();
        item.Reminders.Add(ItemReminder.Offset(10));
        item.Reminders.Add(ItemReminder.Offset(60));
        var before = GoogleMirror.Capture(item);

        var sameInOtherOrder = new[] { ItemReminder.Offset(60).Key, ItemReminder.Offset(10).Key };
        var oneMore = new[] { ItemReminder.Offset(60).Key, ItemReminder.Offset(10).Key, ItemReminder.Offset(1440).Key };

        Assert.Equal(MirroredFields.None, GoogleMirror.Diff(before, GoogleMirror.Capture(item, sameInOtherOrder), isGoogleTask: false));
        Assert.Equal(MirroredFields.Reminders, GoogleMirror.Diff(before, GoogleMirror.Capture(item, oneMore), isGoogleTask: false));
    }

    [Fact]
    public void Diff_GoogleTask_KnowsTheDueDayAndWhetherItIsDone()
    {
        var item = ImportedTask();
        var before = GoogleMirror.Capture(item);

        item.DeadlineUtc = item.DeadlineUtc!.Value.AddHours(15); // same day: not a change
        Assert.Equal(MirroredFields.None, GoogleMirror.Diff(before, GoogleMirror.Capture(item), isGoogleTask: true));

        item.DeadlineUtc = item.DeadlineUtc.Value.AddDays(2);
        item.Status = TaskStatus.Completed;
        item.Title = "Write chapter 4";
        Assert.Equal(
            MirroredFields.Title | MirroredFields.Due | MirroredFields.Completed,
            GoogleMirror.Diff(before, GoogleMirror.Capture(item), isGoogleTask: true));

        // "In progress" is no Google state: Google sees that task as open, so moving between the open states is no change for it.
        Assert.Equal(MirroredFields.None, GoogleMirror.Diff(
            GoogleMirror.Capture(ImportedTask()), GoogleMirror.Capture(ImportedTask(t => t.Status = TaskStatus.InProgress)), isGoogleTask: true));
    }

    // ---- noting an edit ----

    [Fact]
    public void Track_ImportedEvent_NotesTheChangedFieldsAndWhen()
    {
        var item = ImportedEvent();
        var before = GoogleMirror.Capture(item);
        item.Title = "Daily";
        item.Location = "Room 7";

        GoogleMirror.Track(item, before, GoogleMirror.Capture(item), Edited);

        Assert.Equal(MirroredFields.Title | MirroredFields.Location, item.LocalEditedFields);
        Assert.Equal(Edited, item.LocalEditedAtUtc);
    }

    [Fact]
    public void Track_SecondEditBeforeASync_KeepsTheEarlierFields_AndMovesTheTimeToTheLatestEdit()
    {
        var item = ImportedEvent();
        var first = GoogleMirror.Capture(item);
        item.Title = "Daily";
        GoogleMirror.Track(item, first, GoogleMirror.Capture(item), Edited);

        var second = GoogleMirror.Capture(item);
        item.Location = "Room 7";
        GoogleMirror.Track(item, second, GoogleMirror.Capture(item), Edited.AddHours(2));

        Assert.Equal(MirroredFields.Title | MirroredFields.Location, item.LocalEditedFields);
        Assert.Equal(Edited.AddHours(2), item.LocalEditedAtUtc);
    }

    [Fact]
    public void Track_EditThatChangesNothingGoogleHas_NotesNothing()
    {
        var item = ImportedEvent();
        var before = GoogleMirror.Capture(item);
        item.ImpactScore = 8;
        item.EstimatedEffortHours = 2;

        GoogleMirror.Track(item, before, GoogleMirror.Capture(item), Edited);

        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Null(item.LocalEditedAtUtc);
    }

    [Fact]
    public void Track_ItemThatIsNotImported_IsNeverNoted()
    {
        var item = ImportedEvent(i => i.ExternalCalendarEventId = null);
        var before = GoogleMirror.Capture(item);
        item.Title = "Daily";

        GoogleMirror.Track(item, before, GoogleMirror.Capture(item), Edited);

        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
    }

    [Theory]
    [InlineData(EventTemplate.Birthday)]
    [InlineData(EventTemplate.WorkingLocation)]
    public void Track_EventsGoogleManagesItself_AreNeverNoted(EventTemplate template)
    {
        var item = ImportedEvent(i => i.EventTemplate = template);
        var before = GoogleMirror.Capture(item);
        item.Title = "Daily";

        GoogleMirror.Track(item, before, GoogleMirror.Capture(item), Edited);

        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.False(GoogleMirror.IsPushable(item));
    }

    [Theory]
    [InlineData(EventTemplate.Default)]
    [InlineData(EventTemplate.OutOfOffice)]
    [InlineData(EventTemplate.FocusTime)]
    public void IsPushable_PlainEventsAndTheTemplatesGoogleLetsPeopleEdit(EventTemplate template) =>
        Assert.True(GoogleMirror.IsPushable(ImportedEvent(i => i.EventTemplate = template)));

    [Fact]
    public void Settle_ForgetsTheWaitingEdit()
    {
        var item = ImportedEvent(i => { i.LocalEditedFields = MirroredFields.Title; i.LocalEditedAtUtc = Edited; });

        GoogleMirror.Settle(item);

        Assert.Equal(MirroredFields.None, item.LocalEditedFields);
        Assert.Null(item.LocalEditedAtUtc);
    }

    // ---- last write wins ----

    [Theory]
    [InlineData("2026-11-01T13:00:00Z", true)]   // Google changed it after the edit in the app
    [InlineData("2026-11-01T11:00:00Z", false)]  // the edit in the app is later
    [InlineData("2026-11-01T12:00:00Z", false)]  // a tie goes to the app: nothing proves Google is newer
    public void GoogleIsNewer_ComparesTheTwoUpdatedAtTimes(string googleUpdated, bool expected) =>
        Assert.Equal(expected, GoogleMirror.GoogleIsNewer(DateTime.Parse(googleUpdated).ToUniversalTime(), Edited));

    [Fact]
    public void GoogleIsNewer_WithoutGoogleTime_GoogleWins() => Assert.True(GoogleMirror.GoogleIsNewer(null, Edited));

    // ---- the change Google receives: events ----

    [Fact]
    public void EventPatch_HoldsOnlyTheFlaggedFields_ThatReallyDifferFromGoogle()
    {
        var item = ImportedEvent(i =>
        {
            i.Title = "Daily";
            i.Location = "Room 7";
            i.Description = "Agenda";   // changed too, but never flagged: not sent
            i.LocalEditedFields = MirroredFields.Title | MirroredFields.Location;
        });

        var patch = GoogleMirror.BuildEventPatch(item, InGoogle());

        Assert.NotNull(patch);
        Assert.Equal("Daily", patch.Title);
        Assert.Equal("Room 7", patch.Location);
        Assert.Null(patch.Description);
        Assert.Null(patch.Time);
        Assert.Null(patch.ColorId);
        Assert.Null(patch.IsFree);
        Assert.Null(patch.ReminderMinutes);
    }

    [Fact]
    public void EventPatch_FieldsAlreadyEqualToGoogle_GiveNoPatchAtAll()
    {
        var item = ImportedEvent(i =>
        {
            i.Title = "Standup";
            i.LocalEditedFields = MirroredFields.Title | MirroredFields.Time | MirroredFields.Busy;
        });

        Assert.Null(GoogleMirror.BuildEventPatch(item, InGoogle()));
    }

    [Fact]
    public void EventPatch_KeepsThePlanflowPartOfTheTitleAndTheDescription()
    {
        var item = ImportedEvent(i =>
        {
            i.Title = "Daily";
            i.Description = "New agenda";
            i.LocalEditedFields = MirroredFields.Title | MirroredFields.Description;
        });
        var google = InGoogle(g => g with { Title = "Standup [planflow] impact=6", Description = "Old agenda<br>[planflow] impact=8 effort=2h" });

        var patch = GoogleMirror.BuildEventPatch(item, google)!;

        Assert.Equal("Daily [planflow] impact=6", patch.Title);
        Assert.Equal("New agenda\n[planflow] impact=8 effort=2h", patch.Description);
    }

    [Fact]
    public void EventPatch_DescriptionLeftAsGoogleHasIt_IsNotRewritten_SoRichTextSurvives()
    {
        var item = ImportedEvent(i =>
        {
            i.Title = "Daily";
            i.Description = "Agenda"; // the plain text of what Google already has
            i.LocalEditedFields = MirroredFields.Title | MirroredFields.Description;
        });
        var google = InGoogle(g => g with { Description = "<b>Agenda</b><br>[planflow] impact=8" });

        var patch = GoogleMirror.BuildEventPatch(item, google)!;

        Assert.Equal("Daily", patch.Title);
        Assert.Null(patch.Description);
    }

    [Fact]
    public void EventPatch_ClearedDescriptionAndPlace_AreSentAsEmpty_ButTheTagStays()
    {
        var item = ImportedEvent(i =>
        {
            i.Description = null;
            i.Location = null;
            i.LocalEditedFields = MirroredFields.Description | MirroredFields.Location;
        });
        var google = InGoogle(g => g with { Description = "Agenda\n[planflow] impact=8", Location = "Room 1" });

        var patch = GoogleMirror.BuildEventPatch(item, google)!;

        Assert.Equal("[planflow] impact=8", patch.Description);
        Assert.Equal(string.Empty, patch.Location);
    }

    [Fact]
    public void EventPatch_Time_GoesOutAsOnePiece_AndAnUnknownEndGetsTheAssumedLength()
    {
        var timed = ImportedEvent(i => { i.StartUtc = Start.AddHours(1); i.EndUtc = null; i.LocalEditedFields = MirroredFields.Time; });
        var allDay = ImportedEvent(i =>
        {
            i.IsAllDay = true;
            i.StartUtc = Start.Date;
            i.EndUtc = null;
            i.LocalEditedFields = MirroredFields.Time;
        });

        var timedPatch = GoogleMirror.BuildEventPatch(timed, InGoogle())!;
        var allDayPatch = GoogleMirror.BuildEventPatch(allDay, InGoogle())!;

        Assert.Equal(new GoogleEventTime(Start.AddHours(1), Start.AddHours(2), IsAllDay: false), timedPatch.Time);
        Assert.Equal(new GoogleEventTime(Start.Date, Start.Date.AddDays(1), IsAllDay: true), allDayPatch.Time);
    }

    [Fact]
    public void EventPatch_Colour_MapsBackToGoogleId_AppOnlyColourIsSkipped_DefaultClearsIt()
    {
        var banana = ImportedEvent(i => { i.ColorKey = "banana"; i.LocalEditedFields = MirroredFields.Color; });
        var appOnly = ImportedEvent(i => { i.ColorKey = "cocoa"; i.LocalEditedFields = MirroredFields.Color; });
        var cleared = ImportedEvent(i => { i.ColorKey = null; i.LocalEditedFields = MirroredFields.Color; });
        var google = InGoogle(g => g with { ColorId = "9" });

        Assert.Equal("5", GoogleMirror.BuildEventPatch(banana, google)!.ColorId);
        Assert.Null(GoogleMirror.BuildEventPatch(appOnly, google));          // Google has no such colour: nothing to send
        Assert.Equal(string.Empty, GoogleMirror.BuildEventPatch(cleared, google)!.ColorId);
        Assert.Null(GoogleMirror.BuildEventPatch(cleared, InGoogle()));       // already the calendar's colour
    }

    [Fact]
    public void EventPatch_BusyBecomesTransparency()
    {
        var free = ImportedEvent(i => { i.IsBusy = false; i.LocalEditedFields = MirroredFields.Busy; });
        var busy = ImportedEvent(i => { i.IsBusy = true; i.LocalEditedFields = MirroredFields.Busy; });

        Assert.True(GoogleMirror.BuildEventPatch(free, InGoogle())!.IsFree);
        Assert.False(GoogleMirror.BuildEventPatch(busy, InGoogle(g => g with { IsFree = true }))!.IsFree);
        Assert.Null(GoogleMirror.BuildEventPatch(busy, InGoogle()));
    }

    [Fact]
    public void EventPatch_Reminders_AreSentAsMinutesBeforeTheStart()
    {
        var timed = ImportedEvent(i =>
        {
            i.Reminders.Add(ItemReminder.Offset(60));
            i.Reminders.Add(ItemReminder.Offset(10));
            i.LocalEditedFields = MirroredFields.Reminders;
        });
        var allDay = ImportedEvent(i =>
        {
            i.IsAllDay = true;
            i.Reminders.Add(ItemReminder.Day(1, 540));   // the day before at 09:00 = 15 h before midnight
            i.LocalEditedFields = MirroredFields.Reminders;
        });

        Assert.Equal([10, 60], GoogleMirror.BuildEventPatch(timed, InGoogle())!.ReminderMinutes);
        Assert.Equal([900], GoogleMirror.BuildEventPatch(allDay, InGoogle())!.ReminderMinutes);
        Assert.Null(GoogleMirror.BuildEventPatch(timed, InGoogle(g => g with { ReminderMinutes = [60, 10, 10] }))); // the same set
    }

    [Fact]
    public void EventPatch_ReminderRemovedInTheApp_BecomesAnEmptyList()
    {
        var item = ImportedEvent(i => i.LocalEditedFields = MirroredFields.Reminders);

        var patch = GoogleMirror.BuildEventPatch(item, InGoogle(g => g with { ReminderMinutes = [30] }))!;

        Assert.Empty(patch.ReminderMinutes!);
    }

    [Fact]
    public void EventPatch_ReminderGoogleDidNotReport_IsSentExplicitly()
    {
        var item = ImportedEvent(i => { i.Reminders.Add(ItemReminder.Offset(10)); i.LocalEditedFields = MirroredFields.Reminders; });

        var patch = GoogleMirror.BuildEventPatch(item, InGoogle(g => g with { ReminderMinutes = null }))!;

        Assert.Equal([10], patch.ReminderMinutes);
    }

    [Fact]
    public void EventPatch_TemplateWithoutAField_NeverSendsIt()
    {
        var item = ImportedEvent(i =>
        {
            i.EventTemplate = EventTemplate.OutOfOffice;   // no description, place, alerts or busy / free
            i.Description = "Back Monday";
            i.Location = "Home";
            i.IsBusy = false;
            i.Reminders.Add(ItemReminder.Offset(10));
            i.LocalEditedFields = MirroredFields.Description | MirroredFields.Location | MirroredFields.Busy | MirroredFields.Reminders;
        });

        Assert.Null(GoogleMirror.BuildEventPatch(item, InGoogle()));
    }

    // ---- the change Google receives: tasks ----

    [Fact]
    public void TaskPatch_HoldsTitleNotesDueDayAndDoneState()
    {
        var item = ImportedTask(t =>
        {
            t.Title = "Write chapter 4";
            t.Description = "Check sources";
            t.DeadlineUtc = new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
            t.Status = TaskStatus.Completed;
            t.LocalEditedFields = MirroredFields.Title | MirroredFields.Description | MirroredFields.Due | MirroredFields.Completed;
        });

        var patch = GoogleMirror.BuildTaskPatch(item, TaskInGoogle())!;

        Assert.Equal("Write chapter 4", patch.Title);
        Assert.Equal("Check sources", patch.Notes);
        Assert.Equal(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), patch.DueDay);   // a whole day
        Assert.True(patch.IsCompleted);
    }

    [Fact]
    public void TaskPatch_ReopenedTask_AsksGoogleToReopenIt()
    {
        var item = ImportedTask(t => t.LocalEditedFields = MirroredFields.Completed);

        var patch = GoogleMirror.BuildTaskPatch(item, TaskInGoogle(g => g with { IsCompleted = true }))!;

        Assert.False(patch.IsCompleted);
    }

    [Fact]
    public void TaskPatch_KeepsThePlanflowPartOfTheTitleAndNotes()
    {
        var item = ImportedTask(t =>
        {
            t.Title = "Write chapter 4";
            t.Description = "Check sources";
            t.LocalEditedFields = MirroredFields.Title | MirroredFields.Description;
        });
        var google = TaskInGoogle(g => g with { Title = "Write chapter [planflow] impact=7 effort=3h", Notes = "Old\n[planflow] impact=7" });

        var patch = GoogleMirror.BuildTaskPatch(item, google)!;

        Assert.Equal("Write chapter 4 [planflow] impact=7 effort=3h", patch.Title);
        Assert.Equal("Check sources\n[planflow] impact=7", patch.Notes);
    }

    [Fact]
    public void TaskPatch_EverythingAlreadyAsGoogleHasIt_GivesNoPatch()
    {
        var item = ImportedTask(t => t.LocalEditedFields = MirroredFields.Title | MirroredFields.Due | MirroredFields.Completed);

        Assert.Null(GoogleMirror.BuildTaskPatch(item, TaskInGoogle()));
    }

    // ---- small pieces it is built from ----

    [Fact]
    public void TitleWithTagOf_AppendsTheTagPartOfGoogleTitle_OnlyWhenThereIsOne()
    {
        Assert.Equal("Daily [planflow] impact=6 effort=1h", PlanflowTagParser.TitleWithTagOf("Daily", "Standup [planflow] impact=6 effort=1h"));
        Assert.Equal("Daily", PlanflowTagParser.TitleWithTagOf("Daily", "Standup"));
        Assert.Equal("Daily", PlanflowTagParser.TitleWithTagOf("Daily", null));
    }

    [Fact]
    public void DescriptionWithTagOf_PutsTheTagLinesAfterTheNewText()
    {
        Assert.Equal("Agenda\n[planflow] impact=8", PlanflowTagParser.DescriptionWithTagOf("Agenda", "Old<br>[planflow] impact=8<br>more"));
        Assert.Equal("[planflow] impact=8", PlanflowTagParser.DescriptionWithTagOf(null, "[planflow] impact=8"));
        Assert.Equal("Agenda", PlanflowTagParser.DescriptionWithTagOf("Agenda", "Old text"));
        Assert.Equal(string.Empty, PlanflowTagParser.DescriptionWithTagOf(null, "Old text"));
    }

    [Theory]
    [InlineData(false, 10, null, null, 10)]
    [InlineData(true, null, 1, 540, 900)]       // the day before at 09:00
    [InlineData(true, null, 0, 540, -540)]      // the day itself at 09:00: later than midnight
    [InlineData(true, null, 7, 0, 10080)]
    public void ToMinutes_IsTheReverseOfMap(bool allDay, int? offset, int? days, int? timeOfDay, int expectedMinutes)
    {
        var reminder = allDay ? ItemReminder.Day(days!.Value, timeOfDay!.Value) : ItemReminder.Offset(offset!.Value);

        var minutes = GoogleReminderMapper.ToMinutes([reminder.Key]);

        Assert.Equal([expectedMinutes], minutes);
        var back = Assert.Single(GoogleReminderMapper.Map(minutes, allDay));
        Assert.Equal(reminder.Key, back.Key);
    }

    [Fact]
    public void ToMinutes_LeavesOutWhatCountsBackFromADeadline()
    {
        var reminders = new[] { ItemReminder.Offset(30, ReminderAnchor.Deadline).Key, ItemReminder.Offset(10).Key };

        Assert.Equal([10], GoogleReminderMapper.ToMinutes(reminders));
    }

    [Theory]
    [InlineData("lavender", "1")]
    [InlineData("tomato", "11")]
    [InlineData("graphite", "8")]
    public void GoogleColourIds_RoundTrip(string key, string colorId)
    {
        Assert.True(ItemColors.TryToGoogleColorId(key, out var id));
        Assert.Equal(colorId, id);
        Assert.Equal(key, ItemColors.FromGoogleColorId(id));
    }

    [Fact]
    public void EveryColourGoogleKnows_HasAnAppColourAndBack()
    {
        for (var google = 1; google <= 11; google++)
        {
            var key = ItemColors.FromGoogleColorId(google.ToString());
            Assert.NotNull(key);
            Assert.True(ItemColors.TryToGoogleColorId(key, out var id));
            Assert.Equal(google.ToString(), id);
        }
    }

    [Fact]
    public void ColourOnlyTheAppHas_CannotBeSentToGoogle() => Assert.False(ItemColors.TryToGoogleColorId("cocoa", out _));

    [Fact]
    public void DefaultColour_IsSentAsAnEmptyId()
    {
        Assert.True(ItemColors.TryToGoogleColorId(null, out var id));
        Assert.Equal(string.Empty, id);
    }
}
