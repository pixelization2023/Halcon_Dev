namespace MVS.Core
{
    public static class AppContainer
    {
        public static Func<Type, object?>? Resolver { get; set; }

        public static T Resolve<T>()
        {
            if (Resolver == null)
                throw new InvalidOperationException("AppContainer.Resolver 未初始化。");
            return (T)Resolver(typeof(T))!;
        }
    }
}
