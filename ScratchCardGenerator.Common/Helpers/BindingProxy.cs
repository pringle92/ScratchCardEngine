#region Usings

using System.Windows;

#endregion

namespace ScratchCardGenerator.Common.Helpers
{
    #region Binding Proxy

    /// <summary>
    /// A specialised helper class that acts as a proxy to enable data binding from elements 
    /// that do not belong to the logical or visual tree (such as DataGridColumns) to a DataContext 
    /// from an element that does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **The Problem:** In WPF, elements like <c>DataGridColumn</c> are not FrameworkElements and do not inherit a DataContext. 
    /// This makes it impossible to use standard bindings (like RelativeSource or ElementName) to access a ViewModel property from them.
    /// </para>
    /// <para>
    /// **The Solution:** This class inherits from <c>Freezable</c>, a special base class that allows it to participate in the
    /// WPF dependency property system and inherit a DataContext, even when defined as a resource. 
    /// We can declare an instance of this proxy in our XAML resources and bind its 'Data' property to the main DataContext. 
    /// Then, the DataGridColumn can bind to this proxy as a StaticResource, effectively creating a bridge to the main ViewModel.
    /// </para>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// /// <helpers:BindingProxy x:Key="Proxy" Data="{Binding}" />
    /// 
    /// /// <DataGridTextColumn Visibility="{Binding Source={StaticResource Proxy}, Path=Data.IsMyPropertyVisible}" />
    /// ]]>
    /// </code>
    /// </example>
    /// </remarks>
    public class BindingProxy : Freezable
    {
        #region Dependency Properties

        /// <summary>
        /// Defines the 'Data' dependency property, which serves as the target for the proxied data.
        /// </summary>
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(object),
                typeof(BindingProxy),
                new UIPropertyMetadata(null));

        /// <summary>
        /// Gets or sets the data object that this proxy will hold. In typical usage, this is bound to the
        /// main ViewModel of the view.
        /// </summary>
        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        #endregion

        #region Freezable Overrides

        /// <summary>
        /// Creates a new instance of the BindingProxy. This is a required override when inheriting from Freezable.
        /// </summary>
        /// <returns>A new, unfrozen instance of the <see cref="BindingProxy"/> class.</returns>
        protected override Freezable CreateInstanceCore()
        {
            // The Freezable pattern requires this override to enable object cloning and other framework features.
            return new BindingProxy();
        }

        #endregion
    }

    #endregion
}