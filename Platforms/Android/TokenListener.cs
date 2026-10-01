using Android.Gms.Tasks;

namespace SAFETY_STEPS.Platforms.Android
{
    public class TokenListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public TokenListener(TaskCompletionSource<string?> tcs) => _tcs = tcs;

        public void OnComplete(global::Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful)
                _tcs.TrySetResult(task.Result?.ToString());
            else
                _tcs.TrySetResult(null);
        }
    }
}