using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace ReactiveTest.MessagesBus
{
    public class MessageBus
    {
        private readonly ConcurrentDictionary<Type, object> _globalSubjects = new ConcurrentDictionary<Type, object>();
        private readonly ConcurrentDictionary<(Type, string), object> _idSubjects = new ConcurrentDictionary<(Type, string), object>();

        private readonly ConcurrentDictionary<Type, List<(string pattern, Subject<object> subject)>> _wildcardSubjects =
            new ConcurrentDictionary<Type, List<(string pattern, Subject<object> subject)>>();

        // Publish an event globally (all subscribers of T receive it)
        public void Publish<T>(T eventMessage)
        {
            if (_globalSubjects.TryGetValue(typeof(T), out var subjectObj) && subjectObj is Subject<T> subject)
            {
                subject.OnNext(eventMessage);
            }
        }

        // Publish an event for a specific ID (GUID string or name)
        public void Publish<T>(string id, T eventMessage)
        {
            if (_idSubjects.TryGetValue((typeof(T), id), out var subjectObj) && subjectObj is Subject<T> subject)
            {
                subject.OnNext(eventMessage);
            }

            // Notify wildcard subscribers
            if (_wildcardSubjects.TryGetValue(typeof(T), out var wildcardList))
            {
                foreach (var (pattern, subject2) in wildcardList)
                {
                    if (MatchesWildcard(id, pattern))
                    {
                        subject2.OnNext(eventMessage);
                    }
                }
            }
        }

        // Subscribe globally (receives all T events)
        public IObservable<T> GetEvent<T>()
        {
            var subject = (Subject<T>)_globalSubjects.GetOrAdd(typeof(T), _ => new Subject<T>());
            return subject.AsObservable();
        }

        // Subscribe to events for a specific ID (GUID string or name)
        public IObservable<T> GetEvent<T>(string id)
        {
            var subject = (Subject<T>)_idSubjects.GetOrAdd((typeof(T), id), _ => new Subject<T>());
            return subject.AsObservable();
        }

        // Subscribe using a wildcard pattern
        public IObservable<T> GetEventWildcard<T>(string pattern)
        {
            var wildcardList = _wildcardSubjects.GetOrAdd(typeof(T), _ => new List<(string, Subject<object>)>());
            var subject = new Subject<object>();
            wildcardList.Add((pattern, subject));

            return subject.OfType<T>()
                          .AsObservable();
        }

        // Wait for a specific event globally
        public async Task<T> WaitForEvent<T>(Func<T, bool> predicate = null)
        {
            if (predicate == null)
                predicate = _ => true; // Default to accepting any event of type T
            return await GetEvent<T>()
                        .Where(predicate)
                        .FirstAsync()
                        .ToTask();
        }

        // Wait for a specific event from a given ID
        public async Task<T> WaitForEvent<T>(string id, Func<T, bool> predicate = null)
        {
            if (predicate == null)
                predicate = _ => true; // Default to accepting any event of type T
            return await GetEvent<T>(id)
                        .Where(predicate)
                        .FirstAsync()
                        .ToTask();
        }

        // Helper method to match wildcard patterns
        private bool MatchesWildcard(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
                return true;
            if (!pattern.Contains("*"))
                return input == pattern;

            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                                           .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern);
        }
    }
}