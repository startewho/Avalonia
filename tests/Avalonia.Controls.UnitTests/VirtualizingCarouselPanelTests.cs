﻿using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Moq;
using Xunit;

#nullable enable

namespace Avalonia.Controls.UnitTests
{
    public class VirtualizingCarouselPanelTests
    {
        [Fact]
        public void Initial_Item_Is_Displayed()
        {
            using var app = Start();
            var items = new[] { "foo", "bar" };
            var (target, _) = CreateTarget(items);

            Assert.Single(target.Children);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);
            Assert.Equal("foo", container.Content);
        }

        [Fact]
        public void Displays_Next_Item()
        {
            using var app = Start();
            var items = new[] { "foo", "bar" };
            var (target, carousel) = CreateTarget(items);

            carousel.SelectedIndex = 1;
            Layout(target);

            Assert.Single(target.Children);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);
            Assert.Equal("bar", container.Content);
        }

        [Fact]
        public void Handles_Inserted_Item()
        {
            using var app = Start();
            var items = new ObservableCollection<string> { "foo", "bar" };
            var (target, carousel) = CreateTarget(items);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);

            items.Insert(0, "baz");
            Layout(target);

            Assert.Single(target.Children);
            Assert.Same(container, target.Children[0]);
            Assert.Equal("foo", container.Content);
        }

        [Fact]
        public void Handles_Removed_Item()
        {
            using var app = Start();
            var items = new ObservableCollection<string> { "foo", "bar" };
            var (target, carousel) = CreateTarget(items);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);

            items.RemoveAt(0);
            Layout(target);

            Assert.Single(target.Children);
            Assert.Same(container, target.Children[0]);
            Assert.Equal("bar", container.Content);
        }

        [Fact]
        public void Handles_Replaced_Item()
        {
            using var app = Start();
            var items = new ObservableCollection<string> { "foo", "bar" };
            var (target, carousel) = CreateTarget(items);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);

            items[0] = "baz";
            Layout(target);

            Assert.Single(target.Children);
            Assert.Same(container, target.Children[0]);
            Assert.Equal("baz", container.Content);
        }

        [Fact]
        public void Handles_Moved_Item()
        {
            using var app = Start();
            var items = new ObservableCollection<string> { "foo", "bar" };
            var (target, carousel) = CreateTarget(items);
            var container = Assert.IsType<ContentPresenter>(target.Children[0]);

            items.Move(0, 1);
            Layout(target);

            Assert.Single(target.Children);
            Assert.Same(container, target.Children[0]);
            Assert.Equal("bar", container.Content);
        }

        public class Transitions
        {
            [Fact]
            public void Initial_Item_Does_Not_Start_Transition()
            {
                using var app = Start();
                var items = new Control[] { new Button(), new Canvas() };
                var transition = new Mock<IPageTransition>();
                var (target, _) = CreateTarget(items, transition.Object);

                transition.Verify(x => x.Start(
                        It.IsAny<Visual>(),
                        It.IsAny<Visual>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }

            [Fact]
            public void Changing_SelectedIndex_Starts_Transition()
            {
                using var app = Start();
                var items = new Control[] { new Button(), new Canvas() };
                var transition = new Mock<IPageTransition>();
                var (target, carousel) = CreateTarget(items, transition.Object);

                carousel.SelectedIndex = 1;
                Layout(target);

                transition.Verify(x => x.Start(
                        items[0],
                        items[1],
                        true,
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }

            [Fact]
            public void TransitionFrom_Control_Is_Recycled_When_Transition_Completes()
            {
                using var app = Start();
                var items = new Control[] { new Button(), new Canvas() };
                var transition = new Mock<IPageTransition>();
                var (target, carousel) = CreateTarget(items, transition.Object);
                var transitionTask = new TaskCompletionSource();

                transition.Setup(x => x.Start(
                        items[0],
                        items[1],
                        true,
                        It.IsAny<CancellationToken>()))
                    .Returns(() => transitionTask.Task);
                
                carousel.SelectedIndex = 1;
                Layout(target);

                Assert.Equal(items, target.Children);
                Assert.All(items, x => Assert.True(x.IsVisible));
                
                transitionTask.SetResult();

                Assert.Equal(items, target.Children);
                Assert.False(items[0].IsVisible);
                Assert.True(items[1].IsVisible);
            }
        }

        private static IDisposable Start() => UnitTestApplication.Start(TestServices.MockPlatformRenderInterface);

        private static (VirtualizingCarouselPanel, Carousel) CreateTarget(
            IEnumerable items,
            IPageTransition? transition = null)
        {
            var carousel = new Carousel
            {
                Items = items,
                Template = CarouselTemplate(),
                PageTransition = transition,
            };

            var root = new TestRoot(carousel);
            root.LayoutManager.ExecuteInitialLayoutPass();
            return ((VirtualizingCarouselPanel)carousel.Presenter!.Panel!, carousel);
        }

        private static IControlTemplate CarouselTemplate()
        {
            return new FuncControlTemplate((c, ns) =>
                new ScrollViewer
                {
                    Name = "PART_ScrollViewer",
                    Template = ScrollViewerTemplate(),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    Content = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        [~ItemsPresenter.ItemsPanelProperty] = c[~ItemsControl.ItemsPanelProperty],
                    }.RegisterInNameScope(ns)
                }.RegisterInNameScope(ns));
        }

        private static FuncControlTemplate ScrollViewerTemplate()
        {
            return new FuncControlTemplate<ScrollViewer>((parent, scope) =>
                new Panel
                {
                    Children =
                    {
                        new ScrollContentPresenter
                        {
                            Name = "PART_ContentPresenter",
                            [~ScrollContentPresenter.ContentProperty] = parent.GetObservable(ScrollViewer.ContentProperty).ToBinding(),
                            [~~ScrollContentPresenter.ExtentProperty] = parent[~~ScrollViewer.ExtentProperty],
                            [~~ScrollContentPresenter.OffsetProperty] = parent[~~ScrollViewer.OffsetProperty],
                            [~~ScrollContentPresenter.ViewportProperty] = parent[~~ScrollViewer.ViewportProperty],
                            [~ScrollContentPresenter.CanHorizontallyScrollProperty] = parent[~ScrollViewer.CanHorizontallyScrollProperty],
                            [~ScrollContentPresenter.CanVerticallyScrollProperty] = parent[~ScrollViewer.CanVerticallyScrollProperty],
                        }.RegisterInNameScope(scope),
                        new ScrollBar
                        {
                            Name = "verticalScrollBar",
                            [~ScrollBar.MaximumProperty] = parent[~ScrollViewer.VerticalScrollBarMaximumProperty],
                            [~~ScrollBar.ValueProperty] = parent[~~ScrollViewer.VerticalScrollBarValueProperty],
                        }
                    }
                });
        }

        private static void Layout(Control c) => ((ILayoutRoot)c.GetVisualRoot()!).LayoutManager.ExecuteLayoutPass();
    }
}
