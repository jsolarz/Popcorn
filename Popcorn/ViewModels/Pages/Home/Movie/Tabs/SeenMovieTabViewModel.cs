﻿using System;
using System.Collections.Async;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Threading;
using NuGet;
using Popcorn.Comparers;
using Popcorn.Extensions;
using Popcorn.Helpers;
using Popcorn.Messaging;
using Popcorn.Models.Movie;
using Popcorn.Services.Application;
using Popcorn.Services.Movies.Movie;
using Popcorn.Services.User;

namespace Popcorn.ViewModels.Pages.Home.Movie.Tabs
{
    public class SeenMovieTabViewModel : MovieTabsViewModel
    {
        /// <summary>
        /// Initializes a new instance of the SeenMovieTabViewModel class.
        /// </summary>
        /// <param name="applicationService">Application state</param>
        /// <param name="movieService">Movie service</param>
        /// <param name="userService">Movie history service</param>
        public SeenMovieTabViewModel(IApplicationService applicationService, IMovieService movieService,
            IUserService userService)
            : base(applicationService, movieService, userService,
                () => LocalizationProviderHelper.GetLocalizedValue<string>("SeenTitleTab"))
        {
            Messenger.Default.Register<ChangeSeenMovieMessage>(
                this,
                message =>
                {
                    Task.Run(async () =>
                    {
                        var movies = UserService.GetSeenMovies(Page);
                        MaxNumberOfMovies = movies.nbMovies;
                        NeedSync = true;
                        await LoadMoviesAsync();
                    });
                });
        }

        /// <summary>
        /// Load movies asynchronously
        /// </summary>
        public override async Task LoadMoviesAsync(bool reset = false)
        {
            await LoadingSemaphore.WaitAsync();
            StopLoadingMovies();
            if (reset)
            {
                Movies.Clear();
                Page = 0;
            }

            var watch = Stopwatch.StartNew();
            Page++;
            if (Page > 1 && Movies.Count == MaxNumberOfMovies)
            {
                Page--;
                LoadingSemaphore.Release();
                return;
            }

            Logger.Info(
                $"Loading movies seen page {Page}...");
            HasLoadingFailed = false;
            try
            {
                IsLoadingMovies = true;
                var imdbIds = UserService.GetSeenMovies(Page);
                if (!NeedSync)
                {
                    var movies = new List<MovieLightJson>();
                    await imdbIds.movies.ParallelForEachAsync(async imdbId =>
                    {
                        try
                        {
                            var movie = await MovieService.GetMovieLightAsync(imdbId);
                            if (movie != null)
                            {
                                movie.IsFavorite = true;
                                movies.Add(movie);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                    });
                    var updatedMovies = movies.OrderBy(a => a.Title)
                        .Where(a => (Genre == null || a.Genres.Contains(Genre.EnglishName)) &&
                                    a.Rating >= Rating);

                    foreach (var movie in updatedMovies.Except(Movies.ToList(), new MovieLightComparer()))
                    {
                        var pair = Movies
                            .Select((value, index) => new {value, index})
                            .FirstOrDefault(x => string.CompareOrdinal(x.value.Title, movie.Title) > 0);

                        if (pair == null)
                        {
                            Movies.Add(movie);
                        }
                        else
                        {
                            Movies.Insert(pair.index, movie);
                        }
                    }
                }
                else
                {
                    var moviesToDelete = Movies.Select(a => a.ImdbCode).Except(imdbIds.allMovies);
                    var moviesToAdd = imdbIds.allMovies.Except(Movies.Select(a => a.ImdbCode));

                    foreach (var movie in moviesToDelete.ToList())
                    {
                        Movies.Remove(Movies.FirstOrDefault(a => a.ImdbCode == movie));
                    }

                    var movies = moviesToAdd.ToList();
                    var moviesToAddAndToOrder = new List<MovieLightJson>();
                    await movies.ParallelForEachAsync(async imdbId =>
                    {
                        try
                        {
                            var movie = await MovieService.GetMovieLightAsync(imdbId);
                            if ((Genre == null || movie.Genres.Contains(Genre.EnglishName)) && movie.Rating >= Rating)
                            {
                                moviesToAddAndToOrder.Add(movie);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                    });

                    foreach (var movie in moviesToAddAndToOrder.Except(Movies.ToList(), new MovieLightComparer()))
                    {
                        var pair = Movies
                            .Select((value, index) => new {value, index})
                            .FirstOrDefault(x => string.CompareOrdinal(x.value.Title, movie.Title) > 0);

                        if (pair == null)
                        {
                            Movies.Add(movie);
                        }
                        else
                        {
                            Movies.Insert(pair.index, movie);
                        }
                    }
                }

                IsLoadingMovies = false;
                IsMovieFound = Movies.Any();
                CurrentNumberOfMovies = Movies.Count;
                MaxNumberOfMovies = imdbIds.nbMovies;
                UserService.SyncMovieHistory(Movies);
            }
            catch (Exception exception)
            {
                Page--;
                Logger.Error(
                    $"Error while loading movies seen page {Page}: {exception.Message}");
                HasLoadingFailed = true;
                Messenger.Default.Send(new ManageExceptionMessage(exception));
            }
            finally
            {
                NeedSync = false;
                watch.Stop();
                var elapsedMs = watch.ElapsedMilliseconds;
                Logger.Info(
                    $"Loaded movies seen page {Page} in {elapsedMs} milliseconds.");
                LoadingSemaphore.Release();
            }
        }
    }
}