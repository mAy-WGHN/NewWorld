namespace Workforce.Core.WorkingTime;

/// <summary>Plus hours that are still there, from the day they were earned; they expire after the period HR set.</summary>
public sealed record AccountBucket(DateOnly EarnedOn, decimal Remaining, DateOnly? ExpiresOn);

/// <summary>The state of the account on a day. <see cref="Balance"/> = hours still there minus <see cref="Deficit"/>.</summary>
public sealed record AccountState(decimal Balance, decimal Deficit, decimal Forfeited, IReadOnlyList<AccountBucket> Buckets);

/// <summary>
/// The balance of the working time account, computed from the bookings (like the vacation account, so a changed rule applies to everything entered):
/// first in, first out. Plus hours form buckets with their earning date; minus bookings use the oldest bucket that has not expired on that day; what
/// is missing is a deficit that later plus hours cover first. Hours expire on the day after earning date + expiry period; the expired rest is reported as forfeited.
/// Only bookings up to the reference day count.
/// </summary>
public static class WorkingTimeAccount
{
    public static AccountState Compute(IEnumerable<TimeBooking> bookings, WorkingTimeRules rules, DateOnly asOf)
    {
        var buckets = new List<(DateOnly EarnedOn, decimal Remaining)>();
        var deficit = 0m;
        var forfeited = 0m;

        void Expire(DateOnly day)
        {
            if (rules.ExpiryMonths is not { } months)
            {
                return;
            }

            for (var i = 0; i < buckets.Count;)
            {
                if (buckets[i].EarnedOn.AddMonths(months) < day)
                {
                    forfeited += buckets[i].Remaining;
                    buckets.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        foreach (var booking in bookings.Where(b => b.Date <= asOf).OrderBy(b => b.Date).ThenBy(b => b.CreatedAt).ThenBy(b => b.Id))
        {
            Expire(booking.Date);
            var hours = booking.Hours;
            if (hours > 0)
            {
                var covered = Math.Min(deficit, hours);
                deficit -= covered;
                hours -= covered;
                if (hours > 0)
                {
                    buckets.Add((booking.Date, hours));
                }
            }
            else if (hours < 0)
            {
                var need = -hours;
                for (var i = 0; i < buckets.Count && need > 0;)
                {
                    var use = Math.Min(buckets[i].Remaining, need);
                    need -= use;
                    if (buckets[i].Remaining - use == 0)
                    {
                        buckets.RemoveAt(i);
                    }
                    else
                    {
                        buckets[i] = (buckets[i].EarnedOn, buckets[i].Remaining - use);
                        i++;
                    }
                }

                deficit += need;
            }
        }

        Expire(asOf);
        var result = buckets.OrderBy(b => b.EarnedOn).Select(b => new AccountBucket(b.EarnedOn, b.Remaining, rules.ExpiryMonths is { } m ? b.EarnedOn.AddMonths(m) : null)).ToList();
        return new AccountState(result.Sum(b => b.Remaining) - deficit, deficit, forfeited, result);
    }
}
