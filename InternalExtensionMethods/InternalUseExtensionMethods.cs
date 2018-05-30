using System;
using System.Collections.Generic;

namespace InternalExtensionMethods
{
    // ReSharper disable once InconsistentNaming
    public static class This_class_should_never_appear_in_release_code_if_you_see_this_message_please_inform_the_SimpleTamper_devs
    {
        public static string Join(this IEnumerable<string> self, string with)
            => string.Join(with, self);
    }
}