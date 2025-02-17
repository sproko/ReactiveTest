using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace ReactiveTest.MessagesBus
{
    public class MessageBus
    {
        private readonly ConcurrentDictionary<Type, object> _subjects = new ConcurrentDictionary<Type, object>();

        public void Publish<T>(T eventMessage)
        {
            if (_subjects.TryGetValue(typeof(T), out var subjectObj) && subjectObj is Subject<T> subject)
            {
                subject.OnNext(eventMessage);
            }
        }

        public IObservable<T> GetEvent<T>()
        {
            var subject = (Subject<T>)_subjects.GetOrAdd(typeof(T), _ => new Subject<T>());
            return subject.AsObservable();
        }

        public async Task<T> WaitForEvent<T>(Func<T, bool> predicate = null)
        {
            if (predicate == null)
                predicate = _ => true; // Default to accepting any event of type T

            return await GetEvent<T>().Where(predicate).FirstAsync().ToTask();
        }
    }
}