using NUnit.Framework;
using EchoRealm.Film;

namespace EchoRealm.Film.Tests
{
    public class TransientCommandsTests
    {
        [Test]
        public void Earthquake_And_Lightning_AreTransient()
        {
            Assert.IsTrue(TransientCommands.IsTransient("earthquake"));
            Assert.IsTrue(TransientCommands.IsTransient("lightning"));
        }

        [Test]
        public void CharacterAnimations_AreTransient()
        {
            Assert.IsTrue(TransientCommands.IsTransient("dobby_dance"));
            Assert.IsTrue(TransientCommands.IsTransient("astronaut_jump"));
        }

        [Test]
        public void PersistentToggles_AreNotTransient()
        {
            Assert.IsFalse(TransientCommands.IsTransient("rain"));
            Assert.IsFalse(TransientCommands.IsTransient("night"));
            Assert.IsFalse(TransientCommands.IsTransient("grow_tree"));
        }

        [Test]
        public void IsTransient_IsCaseInsensitive_AndNullSafe()
        {
            Assert.IsTrue(TransientCommands.IsTransient("EARTHQUAKE"));
            Assert.IsFalse(TransientCommands.IsTransient(null));
        }
    }
}
