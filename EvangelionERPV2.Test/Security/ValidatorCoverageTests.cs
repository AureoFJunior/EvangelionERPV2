using EvangelionERPV2.Web.Controllers;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every request body that reaches a controller must have a validator, and the registration must
    /// find it. The old hand-written list in <c>Program.cs</c> made both silent when they were false:
    /// a validator that existed but was never registered ran on nothing, and an endpoint with no
    /// validator at all looked exactly the same from the outside.
    /// </summary>
    public class ValidatorCoverageTests
    {
        /// <summary>
        /// Types that carry no field worth validating: the controller reads them directly, or the
        /// value is a primitive the model binder already rejects when malformed.
        /// </summary>
        private static readonly HashSet<Type> BodiesWithoutFields =
        [
            typeof(string),
            typeof(JsonElement)
        ];

        /// <summary>
        /// Bodies EmailController validates itself, before the service is reached:
        /// <c>IsValidEmailSettingsPayload</c> for the settings row and the recipient/attachment
        /// checks on the send path, both covered by EmailController*ValidationTests. A FluentValidation
        /// rule set here would be a second copy of the same limits, free to drift from the one that
        /// actually runs — so they are exempt, not duplicated.
        /// </summary>
        private static readonly HashSet<Type> ValidatedInsideTheController =
        [
            typeof(EvangelionERPV2.Shared.Entities.Email),
            typeof(EvangelionERPV2.Shared.Entities.EmailStructure)
        ];

        [Fact]
        public void EveryBoundRequestType_HasAValidator()
        {
            var webAssembly = typeof(UserController).Assembly;
            var validated = ValidatedTypes(webAssembly);
            validated.UnionWith(ValidatedInsideTheController);

            var missing = BoundRequestTypes(webAssembly)
                .Where(type => !validated.Contains(type))
                .Select(type => type.FullName)
                .Order()
                .ToList();

            Assert.True(
                missing.Count == 0,
                $"Request types bound by a controller with no validator: {string.Join(", ", missing)}");
        }

        [Fact]
        public void EveryValidatorInTheAssembly_IsDiscoverableByTheScan()
        {
            var webAssembly = typeof(UserController).Assembly;

            var declared = webAssembly.GetTypes()
                .Where(IsValidator)
                .ToList();

            var scanned = AssemblyScanner.FindValidatorsInAssembly(webAssembly)
                .Select(result => result.ValidatorType)
                .ToHashSet();

            // Guards the swap from the hand list to AddValidatorsFromAssemblyContaining: if the scan
            // ever stopped seeing a validator, every rule in it would go quiet with nothing failing.
            var invisible = declared
                .Where(type => !scanned.Contains(type))
                .Select(type => type.Name)
                .Order()
                .ToList();

            Assert.True(
                invisible.Count == 0,
                $"Validators the assembly scan does not find: {string.Join(", ", invisible)}");
            Assert.NotEmpty(declared);
        }

        private static bool IsValidator(Type type)
        {
            if (type.IsAbstract || !type.IsClass)
                return false;

            for (var current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                    return true;
            }

            return false;
        }

        /// <summary>The set of T for which some <c>AbstractValidator&lt;T&gt;</c> exists.</summary>
        private static HashSet<Type> ValidatedTypes(Assembly webAssembly)
        {
            var validated = new HashSet<Type>();

            foreach (var validator in webAssembly.GetTypes().Where(IsValidator))
            {
                for (var current = validator.BaseType; current != null; current = current.BaseType)
                {
                    if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                    {
                        validated.Add(current.GetGenericArguments()[0]);
                        break;
                    }
                }
            }

            return validated;
        }

        /// <summary>Every type a controller action accepts as a request body or form.</summary>
        private static IEnumerable<Type> BoundRequestTypes(Assembly webAssembly)
        {
            var controllers = webAssembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)
                    || (!type.IsAbstract && type.Name.EndsWith("Controller", StringComparison.Ordinal)));

            foreach (var controller in controllers)
            {
                var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var parameter in actions.SelectMany(action => action.GetParameters()))
                {
                    var isBound = parameter.GetCustomAttribute<FromBodyAttribute>() != null
                        || parameter.GetCustomAttribute<FromFormAttribute>() != null;

                    if (!isBound)
                        continue;

                    var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                    if (type.IsPrimitive || type.IsEnum || BodiesWithoutFields.Contains(type))
                        continue;

                    yield return type;
                }
            }
        }
    }
}
