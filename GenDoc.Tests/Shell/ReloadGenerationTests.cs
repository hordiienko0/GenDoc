using GenDoc.ViewModels.Shell;

namespace GenDoc.Tests.Shell;

// «Комплектність» і «Особовий склад» перезавантажують список у стилі
// «запустив і забув»: зміна набору, пакета чи вузла дерева не чекає на
// попередній запит. Швидке перемикання лишало два запити в польоті, і
// повільніший приходив ОСТАННІМ - на екрані опинявся список попереднього
// вибору, тоді як комбо й дерево показували новий (аудит 2026-08-28).
//
// Скасувати сам запит нема чим - сервіси токена не приймають, - але й не
// треба: досить не застосовувати відповідь, що вже застаріла.
public class ReloadGenerationTests
{
    [Fact]
    public void TheOnlyRunInFlightIsCurrent()
    {
        var generation = new ReloadGeneration();

        var token = generation.Begin();

        Assert.True(generation.IsCurrent(token));
    }

    [Fact]
    public void AnEarlierRunStopsBeingCurrentOnceAnotherStarts()
    {
        var generation = new ReloadGeneration();

        var first = generation.Begin();
        var second = generation.Begin();

        Assert.False(generation.IsCurrent(first));
        Assert.True(generation.IsCurrent(second));
    }

    [Fact]
    public void ATokenNeverBecomesCurrentAgain()
    {
        var generation = new ReloadGeneration();
        var first = generation.Begin();
        generation.Begin();

        generation.Begin();

        Assert.False(generation.IsCurrent(first));
    }

    // Головний випадок: перший запит повертається ОСТАННІМ. Саме він і псував
    // екран, бо приходив уже після відповіді на актуальний вибір.
    [Fact]
    public async Task AStaleAnswerArrivingLast_IsNotApplied()
    {
        var generation = new ReloadGeneration();
        var applied = new List<string>();

        var slow = new TaskCompletionSource<string>();
        var fast = new TaskCompletionSource<string>();

        async Task LoadAsync(Task<string> answer)
        {
            var token = generation.Begin();
            var result = await answer;
            if (!generation.IsCurrent(token)) return;
            applied.Add(result);
        }

        var first = LoadAsync(slow.Task);
        var second = LoadAsync(fast.Task);

        fast.SetResult("новий вибір");
        await second;

        slow.SetResult("попередній вибір");
        await first;

        Assert.Equal(new[] { "новий вибір" }, applied);
    }

    // Звичайний порядок не ламається: один запит - одна відповідь.
    [Fact]
    public async Task AnAnswerToTheCurrentRequest_IsApplied()
    {
        var generation = new ReloadGeneration();
        var applied = new List<string>();

        var token = generation.Begin();
        var result = await Task.FromResult("вибір");
        if (generation.IsCurrent(token)) applied.Add(result);

        Assert.Equal(new[] { "вибір" }, applied);
    }

    // Прапорець зайнятості гасить лише актуальний прогін: інакше перший, що
    // завершився, прибирав би індикатор, поки другий ще працює.
    [Fact]
    public async Task OnlyTheCurrentRunClearsTheBusyFlag()
    {
        var generation = new ReloadGeneration();
        var busy = false;

        var slow = new TaskCompletionSource<string>();
        var fast = new TaskCompletionSource<string>();

        async Task LoadAsync(Task<string> answer)
        {
            var token = generation.Begin();
            busy = true;
            try
            {
                await answer;
            }
            finally
            {
                if (generation.IsCurrent(token)) busy = false;
            }
        }

        var first = LoadAsync(slow.Task);
        var second = LoadAsync(fast.Task);

        slow.SetResult("перший");
        await first;

        Assert.True(busy);

        fast.SetResult("другий");
        await second;

        Assert.False(busy);
    }
}
