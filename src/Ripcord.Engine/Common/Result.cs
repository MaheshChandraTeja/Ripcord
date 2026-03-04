using System;

namespace Ripcord.Engine.Common
{
    /// <summary>Result monad with error message and optional exception.</summary>
    public readonly struct Result
    {
        public bool Success { get; }
        public string? Error { get; }
        public Exception? Exception { get; }

        private Result(bool success, string? error, Exception? ex)
        { Success = success; Error = error; Exception = ex; }

        public static Result Ok() => new(true, null, null);
        public static Result Fail(string error, Exception? ex = null) => new(false, error, ex);

        public override string ToString() => Success ? "Ok" : $"Fail: {Error}";
    }

    public readonly struct Result<T>
    {
        public bool Success { get; }
        public string? Error { get; }
        public Exception? Exception { get; }
        public T? Value { get; }

        private Result(bool success, T? value, string? error, Exception? ex)
        { Success = success; Value = value; Error = error; Exception = ex; }

        public static Result<T> Ok(T value) => new(true, value, null, null);
        public static Result<T> Fail(string error, Exception? ex = null) => new(false, default, error, ex);

        public void Deconstruct(out bool ok, out T? value) { ok = Success; value = Value; }

        public override string ToString() => Success ? $"Ok({Value})" : $"Fail: {Error}";
    }
}
