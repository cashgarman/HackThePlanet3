using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
    /// <summary>
    ///     Interface glyphs from Font Awesome Free 6.7.2 by @fontawesome (https://fontawesome.com).
    ///     Icons: CC BY 4.0. Fonts: SIL OFL 1.1. Code: MIT. Copyright 2024 Fonticons, Inc.
    ///     https://fontawesome.com/license/free
    /// </summary>
    public static class HeaderIcons
	{
		private const byte Move = 0;
		private const byte Line = 1;
		private const byte Cubic = 2;
		private const byte Close = 3;

		private static readonly Dictionary<string, Icon> sources = new()
		{
			["filter"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M3.9 54.9C10.5 40.9 24.5 32 40 32l432 0c15.5 0 29.5 8.9 36.1 22.9s4.6 30.5-5.2 42.5L320 320.9 320 448c0 12.1-6.8 23.2-17.7 28.6s-23.8 4.3-33.5-3l-64-48c-8.1-6-12.8-15.5-12.8-25.6l0-79.1L9 97.3C-.7 85.4-2.8 68.8 3.9 54.9z"
			},
			["back"] = new Icon
			{
				Width = 448, Height = 512,
				Path =
					"M9.4 233.4c-12.5 12.5-12.5 32.8 0 45.3l160 160c12.5 12.5 32.8 12.5 45.3 0s12.5-32.8 0-45.3L109.2 288 416 288c17.7 0 32-14.3 32-32s-14.3-32-32-32l-306.7 0L214.6 118.6c12.5-12.5 12.5-32.8 0-45.3s-32.8-12.5-45.3 0l-160 160z"
			},
			["forward"] = new Icon
			{
				Width = 448, Height = 512,
				Path =
					"M438.6 278.6c12.5-12.5 12.5-32.8 0-45.3l-160-160c-12.5-12.5-32.8-12.5-45.3 0s-12.5 32.8 0 45.3L338.8 224 32 224c-17.7 0-32 14.3-32 32s14.3 32 32 32l306.7 0L233.4 393.4c-12.5 12.5-12.5 32.8 0 45.3s32.8 12.5 45.3 0l160-160z"
			},
			["overview"] = new Icon
			{
				Width = 448, Height = 512,
				Path =
					"M32 32C14.3 32 0 46.3 0 64l0 96c0 17.7 14.3 32 32 32s32-14.3 32-32l0-64 64 0c17.7 0 32-14.3 32-32s-14.3-32-32-32L32 32zM64 352c0-17.7-14.3-32-32-32s-32 14.3-32 32l0 96c0 17.7 14.3 32 32 32l96 0c17.7 0 32-14.3 32-32s-14.3-32-32-32l-64 0 0-64zM320 32c-17.7 0-32 14.3-32 32s14.3 32 32 32l64 0 0 64c0 17.7 14.3 32 32 32s32-14.3 32-32l0-96c0-17.7-14.3-32-32-32l-96 0zM448 352c0-17.7-14.3-32-32-32s-32 14.3-32 32l0 64-64 0c-17.7 0-32 14.3-32 32s14.3 32 32 32l96 0c17.7 0 32-14.3 32-32l0-96z"
			},
			["web"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M352 0c-12.9 0-24.6 7.8-29.6 19.8s-2.2 25.7 6.9 34.9L370.7 96 201.4 265.4c-12.5 12.5-12.5 32.8 0 45.3s32.8 12.5 45.3 0L416 141.3l41.4 41.4c9.2 9.2 22.9 11.9 34.9 6.9s19.8-16.6 19.8-29.6l0-128c0-17.7-14.3-32-32-32L352 0zM80 32C35.8 32 0 67.8 0 112L0 432c0 44.2 35.8 80 80 80l320 0c44.2 0 80-35.8 80-80l0-112c0-17.7-14.3-32-32-32s-32 14.3-32 32l0 112c0 8.8-7.2 16-16 16L80 448c-8.8 0-16-7.2-16-16l0-320c0-8.8 7.2-16 16-16l112 0c17.7 0 32-14.3 32-32s-14.3-32-32-32L80 32z"
			},
			["save"] = new Icon
			{
				Width = 448, Height = 512,
				Path =
					"M64 32C28.7 32 0 60.7 0 96L0 416c0 35.3 28.7 64 64 64l320 0c35.3 0 64-28.7 64-64l0-242.7c0-17-6.7-33.3-18.7-45.3L352 50.7C340 38.7 323.7 32 306.7 32L64 32zm0 96c0-17.7 14.3-32 32-32l192 0c17.7 0 32 14.3 32 32l0 64c0 17.7-14.3 32-32 32L96 224c-17.7 0-32-14.3-32-32l0-64zM224 288a64 64 0 1 1 0 128 64 64 0 1 1 0-128z"
			},
			["restore"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M75 75L41 41C25.9 25.9 0 36.6 0 57.9L0 168c0 13.3 10.7 24 24 24l110.1 0c21.4 0 32.1-25.9 17-41l-30.8-30.8C155 85.5 203 64 256 64c106 0 192 86 192 192s-86 192-192 192c-40.8 0-78.6-12.7-109.7-34.4c-14.5-10.1-34.4-6.6-44.6 7.9s-6.6 34.4 7.9 44.6C151.2 495 201.7 512 256 512c141.4 0 256-114.6 256-256S397.4 0 256 0C185.3 0 121.3 28.7 75 75zm181 53c-13.3 0-24 10.7-24 24l0 104c0 6.4 2.5 12.5 7 17l72 72c9.4 9.4 24.6 9.4 33.9 0s9.4-24.6 0-33.9l-65-65 0-94.1c0-13.3-10.7-24-24-24z"
			},
			["help"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M256 512A256 256 0 1 0 256 0a256 256 0 1 0 0 512zM169.8 165.3c7.9-22.3 29.1-37.3 52.8-37.3l58.3 0c34.9 0 63.1 28.3 63.1 63.1c0 22.6-12.1 43.5-31.7 54.8L280 264.4c-.2 13-10.9 23.6-24 23.6c-13.3 0-24-10.7-24-24l0-13.5c0-8.6 4.6-16.5 12.1-20.8l44.3-25.4c4.7-2.7 7.6-7.7 7.6-13.1c0-8.4-6.8-15.1-15.1-15.1l-58.3 0c-3.4 0-6.4 2.1-7.5 5.3l-.4 1.2c-4.4 12.5-18.2 19-30.6 14.6s-19-18.2-14.6-30.6l.4-1.2zM224 352a32 32 0 1 1 64 0 32 32 0 1 1 -64 0z"
			},
			["refresh"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M105.1 202.6c7.7-21.8 20.2-42.3 37.8-59.8c62.5-62.5 163.8-62.5 226.3 0L386.3 160 352 160c-17.7 0-32 14.3-32 32s14.3 32 32 32l111.5 0c0 0 0 0 0 0l.4 0c17.7 0 32-14.3 32-32l0-112c0-17.7-14.3-32-32-32s-32 14.3-32 32l0 35.2L414.4 97.6c-87.5-87.5-229.3-87.5-316.8 0C73.2 122 55.6 150.7 44.8 181.4c-5.9 16.7 2.9 34.9 19.5 40.8s34.9-2.9 40.8-19.5zM39 289.3c-5 1.5-9.8 4.2-13.7 8.2c-4 4-6.7 8.8-8.1 14c-.3 1.2-.6 2.5-.8 3.8c-.3 1.7-.4 3.4-.4 5.1L16 432c0 17.7 14.3 32 32 32s32-14.3 32-32l0-35.1 17.6 17.5c0 0 0 0 0 0c87.5 87.4 229.3 87.4 316.7 0c24.4-24.4 42.1-53.1 52.9-83.8c5.9-16.7-2.9-34.9-19.5-40.8s-34.9 2.9-40.8 19.5c-7.7 21.8-20.2 42.3-37.8 59.8c-62.5 62.5-163.8 62.5-226.3 0l-.1-.1L125.6 352l34.4 0c17.7 0 32-14.3 32-32s-14.3-32-32-32L48.4 288c-1.6 0-3.2 .1-4.8 .3s-3.1 .5-4.6 1z"
			},
			["play"] = new Icon
			{
				Width = 384, Height = 512,
				Path =
					"M73 39c-14.8-9.1-33.4-9.4-48.5-.9S0 62.6 0 80L0 432c0 17.4 9.4 33.4 24.5 41.9s33.7 8.1 48.5-.9L361 297c14.3-8.7 23-24.2 23-41s-8.7-32.2-23-41L73 39z"
			},
			["clock"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M256 0a256 256 0 1 1 0 512A256 256 0 1 1 256 0zM232 120l0 136c0 8 4 15.5 10.7 20l96 64c11 7.4 25.9 4.4 33.3-6.7s4.4-25.9-6.7-33.3L280 243.2 280 120c0-13.3-10.7-24-24-24s-24 10.7-24 24z"
			},
			["import"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M128 64c0-35.3 28.7-64 64-64L352 0l0 128c0 17.7 14.3 32 32 32l128 0 0 288c0 35.3-28.7 64-64 64l-256 0c-35.3 0-64-28.7-64-64l0-112 174.1 0-39 39c-9.4 9.4-9.4 24.6 0 33.9s24.6 9.4 33.9 0l80-80c9.4-9.4 9.4-24.6 0-33.9l-80-80c-9.4-9.4-24.6-9.4-33.9 0s-9.4 24.6 0 33.9l39 39L128 288l0-224zm0 224l0 48L24 336c-13.3 0-24-10.7-24-24s10.7-24 24-24l104 0zM512 128l-128 0L384 0 512 128z"
			},
			["search"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M416 208c0 45.9-14.9 88.3-40 122.7L502.6 457.4c12.5 12.5 12.5 32.8 0 45.3s-32.8 12.5-45.3 0L330.7 376c-34.4 25.2-76.8 40-122.7 40C93.1 416 0 322.9 0 208S93.1 0 208 0S416 93.1 416 208zM208 352a144 144 0 1 0 0-288 144 144 0 1 0 0 288z"
			},
			["close"] = new Icon
			{
				Width = 384, Height = 512,
				Path =
					"M342.6 150.6c12.5-12.5 12.5-32.8 0-45.3s-32.8-12.5-45.3 0L192 210.7 86.6 105.4c-12.5-12.5-32.8-12.5-45.3 0s-12.5 32.8 0 45.3L146.7 256 41.4 361.4c-12.5 12.5-12.5 32.8 0 45.3s32.8 12.5 45.3 0L192 301.3 297.4 406.6c12.5 12.5 32.8 12.5 45.3 0s12.5-32.8 0-45.3L237.3 256 342.6 150.6z"
			},
			["eye"] = new Icon
			{
				Width = 576, Height = 512,
				Path =
					"M288 32c-80.8 0-145.5 36.8-192.6 80.6C48.6 156 17.3 208 2.5 243.7c-3.3 7.9-3.3 16.7 0 24.6C17.3 304 48.6 356 95.4 399.4C142.5 443.2 207.2 480 288 480s145.5-36.8 192.6-80.6c46.8-43.5 78.1-95.4 93-131.1c3.3-7.9 3.3-16.7 0-24.6c-14.9-35.7-46.2-87.7-93-131.1C433.5 68.8 368.8 32 288 32zM144 256a144 144 0 1 1 288 0 144 144 0 1 1 -288 0zm144-64c0 35.3-28.7 64-64 64c-7.1 0-13.9-1.2-20.3-3.3c-5.5-1.8-11.9 1.6-11.7 7.4c.3 6.9 1.3 13.8 3.2 20.7c13.7 51.2 66.4 81.6 117.6 67.9s81.6-66.4 67.9-117.6c-11.1-41.5-47.8-69.4-88.6-71.1c-5.8-.2-9.2 6.1-7.4 11.7c2.1 6.4 3.3 13.2 3.3 20.3z"
			},
			["link"] = new Icon
			{
				Width = 640, Height = 512,
				Path =
					"M579.8 267.7c56.5-56.5 56.5-148 0-204.5c-50-50-128.8-56.5-186.3-15.4l-1.6 1.1c-14.4 10.3-17.7 30.3-7.4 44.6s30.3 17.7 44.6 7.4l1.6-1.1c32.1-22.9 76-19.3 103.8 8.6c31.5 31.5 31.5 82.5 0 114L422.3 334.8c-31.5 31.5-82.5 31.5-114 0c-27.9-27.9-31.5-71.8-8.6-103.8l1.1-1.6c10.3-14.4 6.9-34.4-7.4-44.6s-34.4-6.9-44.6 7.4l-1.1 1.6C206.5 251.2 213 330 263 380c56.5 56.5 148 56.5 204.5 0L579.8 267.7zM60.2 244.3c-56.5 56.5-56.5 148 0 204.5c50 50 128.8 56.5 186.3 15.4l1.6-1.1c14.4-10.3 17.7-30.3 7.4-44.6s-30.3-17.7-44.6-7.4l-1.6 1.1c-32.1 22.9-76 19.3-103.8-8.6C74 372 74 321 105.5 289.5L217.7 177.2c31.5-31.5 82.5-31.5 114 0c27.9 27.9 31.5 71.8 8.6 103.9l-1.1 1.6c-10.3 14.4-6.9 34.4 7.4 44.6s34.4 6.9 44.6-7.4l1.1-1.6C433.5 260.8 427 182 377 132c-56.5-56.5-148-56.5-204.5 0L60.2 244.3z"
			},
			["dim"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M448 256c0-106-86-192-192-192l0 384c106 0 192-86 192-192zM0 256a256 256 0 1 1 512 0A256 256 0 1 1 0 256z"
			},
			["pause"] = new Icon
			{
				Width = 320, Height = 512,
				Path =
					"M48 64C21.5 64 0 85.5 0 112L0 400c0 26.5 21.5 48 48 48l32 0c26.5 0 48-21.5 48-48l0-288c0-26.5-21.5-48-48-48L48 64zm192 0c-26.5 0-48 21.5-48 48l0 288c0 26.5 21.5 48 48 48l32 0c26.5 0 48-21.5 48-48l0-288c0-26.5-21.5-48-48-48l-32 0z"
			},
			["sun"] = new Icon
			{
				Width = 448, Height = 512,
				Path =
					"M159.3 5.4c7.8-7.3 19.9-7.2 27.7 .1c27.6 25.9 53.5 53.8 77.7 84c11-14.4 23.5-30.1 37-42.9c7.9-7.4 20.1-7.4 28 .1c34.6 33 63.9 76.6 84.5 118c20.3 40.8 33.8 82.5 33.8 111.9C448 404.2 348.2 512 224 512C98.4 512 0 404.1 0 276.5c0-38.4 17.8-85.3 45.4-131.7C73.3 97.7 112.7 48.6 159.3 5.4zM225.7 416c25.3 0 47.7-7 68.8-21c42.1-29.4 53.4-88.2 28.1-134.4c-4.5-9-16-9.6-22.5-2l-25.2 29.3c-6.6 7.6-18.5 7.4-24.7-.5c-16.5-21-46-58.5-62.8-79.8c-6.3-8-18.3-8.1-24.7-.1c-33.8 42.5-50.8 69.3-50.8 99.4C112 375.4 162.6 416 225.7 416z"
			},
			["unpin"] = new Icon
			{
				Width = 384, Height = 512,
				Path =
					"M32 32C32 14.3 46.3 0 64 0L320 0c17.7 0 32 14.3 32 32s-14.3 32-32 32l-29.5 0 11.4 148.2c36.7 19.9 65.7 53.2 79.5 94.7l1 3c3.3 9.8 1.6 20.5-4.4 28.8s-15.7 13.3-26 13.3L32 352c-10.3 0-19.9-4.9-26-13.3s-7.7-19.1-4.4-28.8l1-3c13.8-41.5 42.8-74.8 79.5-94.7L93.5 64 64 64C46.3 64 32 49.7 32 32zM160 384l64 0 0 96c0 17.7-14.3 32-32 32s-32-14.3-32-32l0-96z"
			},
			["nodes"] = new Icon
			{
				Width = 576, Height = 512,
				Path =
					"M0 80C0 53.5 21.5 32 48 32l96 0c26.5 0 48 21.5 48 48l0 16 192 0 0-16c0-26.5 21.5-48 48-48l96 0c26.5 0 48 21.5 48 48l0 96c0 26.5-21.5 48-48 48l-96 0c-26.5 0-48-21.5-48-48l0-16-192 0 0 16c0 1.7-.1 3.4-.3 5L272 288l96 0c26.5 0 48 21.5 48 48l0 96c0 26.5-21.5 48-48 48l-96 0c-26.5 0-48-21.5-48-48l0-96c0-1.7 .1-3.4 .3-5L144 224l-96 0c-26.5 0-48-21.5-48-48L0 80z"
			},
			["info"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M256 512A256 256 0 1 0 256 0a256 256 0 1 0 0 512zM216 336l24 0 0-64-24 0c-13.3 0-24-10.7-24-24s10.7-24 24-24l48 0c13.3 0 24 10.7 24 24l0 88 8 0c13.3 0 24 10.7 24 24s-10.7 24-24 24l-80 0c-13.3 0-24-10.7-24-24s10.7-24 24-24zm40-208a32 32 0 1 1 0 64 32 32 0 1 1 0-64z"
			},
			["ask"] = new Icon
			{
				Width = 320, Height = 512,
				Path =
					"M160 0c17.7 0 32 14.3 32 32l0 35.7c1.6 .2 3.1 .4 4.7 .7c.4 .1 .7 .1 1.1 .2l48 8.8c17.4 3.2 28.9 19.9 25.7 37.2s-19.9 28.9-37.2 25.7l-47.5-8.7c-31.3-4.6-58.9-1.5-78.3 6.2s-27.2 18.3-29 28.1c-2 10.7-.5 16.7 1.2 20.4c1.8 3.9 5.5 8.3 12.8 13.2c16.3 10.7 41.3 17.7 73.7 26.3l2.9 .8c28.6 7.6 63.6 16.8 89.6 33.8c14.2 9.3 27.6 21.9 35.9 39.5c8.5 17.9 10.3 37.9 6.4 59.2c-6.9 38-33.1 63.4-65.6 76.7c-13.7 5.6-28.6 9.2-44.4 11l0 33.4c0 17.7-14.3 32-32 32s-32-14.3-32-32l0-34.9c-.4-.1-.9-.1-1.3-.2l-.2 0s0 0 0 0c-24.4-3.8-64.5-14.3-91.5-26.3c-16.1-7.2-23.4-26.1-16.2-42.2s26.1-23.4 42.2-16.2c20.9 9.3 55.3 18.5 75.2 21.6c31.9 4.7 58.2 2 76-5.3c16.9-6.9 24.6-16.9 26.8-28.9c1.9-10.6 .4-16.7-1.3-20.4c-1.9-4-5.6-8.4-13-13.3c-16.4-10.7-41.5-17.7-74-26.3l-2.8-.7s0 0 0 0C119.4 279.3 84.4 270 58.4 253c-14.2-9.3-27.5-22-35.8-39.6c-8.4-17.9-10.1-37.9-6.1-59.2C23.7 116 52.3 91.2 84.8 78.3c13.3-5.3 27.9-8.9 43.2-11L128 32c0-17.7 14.3-32 32-32z"
			},
			["cube"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M234.5 5.7c13.9-5 29.1-5 43.1 0l192 68.6C495 83.4 512 107.5 512 134.6l0 242.9c0 27-17 51.2-42.5 60.3l-192 68.6c-13.9 5-29.1 5-43.1 0l-192-68.6C17 428.6 0 404.5 0 377.4L0 134.6c0-27 17-51.2 42.5-60.3l192-68.6zM256 66L82.3 128 256 190l173.7-62L256 66zm32 368.6l160-57.1 0-188L288 246.6l0 188z"
			},
			["list"] = new Icon
				{ Width = 512, Height = 512, Path = "M48 48H464V144H48zM48 208H464V304H48zM48 368H464V464H48z" },
			["up"] = new Icon { Width = 512, Height = 512, Path = "M96 320L256 128L416 320L352 320L256 208L160 320z" },
			["down"] =
				new Icon { Width = 512, Height = 512, Path = "M96 192L160 192L256 304L352 192L416 192L256 384z" },
			["grip"] = new Icon { Width = 24, Height = 32, Path = "M2 4H8V28H2zM10 4H16V28H10zM18 4H24V28H18z" },
			["copy"] = new Icon
				{ Width = 448, Height = 512, Path = "M192 0H400V320H192zM48 96H160V144H96V416H304V464H48z" },
			["trash"] = new Icon
				{ Width = 448, Height = 512, Path = "M96 32H288L320 80H400V128H48V80H128zM80 160H368L336 480H112z" },
			["check"] =
				new Icon { Width = 512, Height = 512, Path = "M64 268L192 396L448 112L448 176L192 448L64 332z" },
			["bolt"] = new Icon { Width = 512, Height = 512, Path = "M288 16L96 280H208L144 496L416 216H272L288 16z" },
			["spark"] = new Icon { Width = 32, Height = 32, Path = "M16 1L19 13L31 16L19 19L16 31L13 19L1 16L13 13Z" },
			["pen"] = new Icon { Width = 32, Height = 32, Path = "M4 20L12 28L28 12L20 4Z M20 4L24 0L32 8L28 12Z" },
			["bars"] = new Icon { Width = 32, Height = 32, Path = "M2 30V12H9V30Z M12 30V4H19V30Z M22 30V16H29V30Z" },
			["align"] = new Icon { Width = 32, Height = 32, Path = "M2 4H30V9H2Z M6 13H26V18H6Z M11 22H21V27H11Z" },
			["page"] = new Icon { Width = 32, Height = 32, Path = "M6 2H18V10H26V30H6Z" },
			["mail"] = new Icon { Width = 32, Height = 32, Path = "M2 8L16 18L30 8V26H2Z" },
			["focus"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M80 256A176 176 0 1 0 432 256A176 176 0 1 0 80 256ZM128 256A128 128 0 1 1 384 256A128 128 0 1 1 128 256ZM216 256A40 40 0 1 0 296 256A40 40 0 1 0 216 256ZM16 240H64V272H16ZM448 240H496V272H448ZM240 16H272V64H240ZM240 448H272V496H240Z"
			},
			["hop1"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M24 256A96 96 0 1 0 216 256A96 96 0 1 0 24 256ZM296 256A96 96 0 1 0 488 256A96 96 0 1 0 296 256ZM120 240H392V272H120Z"
			},
			["hop2"] = new Icon
			{
				Width = 512, Height = 512,
				Path =
					"M24 256A72 72 0 1 0 168 256A72 72 0 1 0 24 256ZM184 256A72 72 0 1 0 328 256A72 72 0 1 0 184 256ZM344 256A72 72 0 1 0 488 256A72 72 0 1 0 344 256ZM96 240H416V272H96Z"
			},
			["plus"] = new Icon
				{ Width = 512, Height = 512, Path = "M224 64H288V224H448V288H288V448H224V288H64V224H224Z" },
			["star"] = new Icon
			{
				Width = 576, Height = 512,
				Path =
					"M316.9 18C311.6 7 300.4 0 288.1 0s-23.4 7-28.8 18L195 150.3 51.4 171.5c-12 1.8-22 10.2-25.7 21.7s-.7 24.2 7.9 32.7L137.8 329 113.2 474.7c-2 12 3 24.2 12.9 31.3s23 8 33.8 2.3l128.3-68.5 128.3 68.5c10.8 5.7 23.9 4.9 33.8-2.3s14.9-19.3 12.9-31.3L438.5 329 542.7 225.9c8.6-8.5 11.7-21.2 7.9-32.7s-13.7-19.9-25.7-21.7L381.2 150.3 316.9 18z"
			},
			["tri-right"] = new Icon { Width = 512, Height = 512, Path = "M160 96L416 256L160 416Z" },
			["tri-down"] = new Icon { Width = 512, Height = 512, Path = "M96 160L416 160L256 416Z" },
			["jump"] = new Icon
			{
				Width = 320, Height = 512,
				Path =
					"M52.5 440.6c-9.5 7.9-22.8 9.7-34.1 4.4S0 428.4 0 416L0 96C0 83.6 7.2 72.3 18.4 67s24.5-3.6 34.1 4.4l192 160L256 241l0-145c0-17.7 14.3-32 32-32s32 14.3 32 32l0 320c0 17.7-14.3 32-32 32s-32-14.3-32-32l0-145-11.5 9.6-192 160z"
			}
		};

		private static readonly Dictionary<string, List<DrawOp>> cached = new();

		/// <summary>Paints a header icon using the default foreground color.</summary>
		/// <param name="painter">Painter used to draw the icon.</param>
		/// <param name="icon">Icon identifier.</param>
		/// <param name="rect">Bounds for the icon.</param>
		public static void Paint(Painter2D painter, string icon, Rect rect)
		{
			Paint(painter, icon, rect, new Color(217f / 255f, 229f / 255f, 246f / 255f));
		}

		/// <summary>Paints a header icon using the supplied color.</summary>
		/// <param name="painter">Painter used to draw the icon.</param>
		/// <param name="icon">Icon identifier.</param>
		/// <param name="rect">Bounds for the icon.</param>
		/// <param name="color">Icon color.</param>
		public static void Paint(Painter2D painter, string icon, Rect rect, Color color)
		{
			if (rect.width < 1f || rect.height < 1f || !sources.TryGetValue(icon, out var spec)) return;

			// Compile each source path once and reuse its normalized drawing operations across icon sizes.
			if (!cached.TryGetValue(icon, out var ops))
			{
				ops = Compile(spec.Path);
				cached[icon] = ops;
			}

			var inner = new Rect(rect.x + .5f, rect.y + .5f, Mathf.Max(1f, rect.width - 1f),
								 Mathf.Max(1f, rect.height - 1f));

			// Preserve the source aspect ratio and center the path within a half-pixel inset.
			var scale = Mathf.Min(inner.width / spec.Width, inner.height / spec.Height);
			var ox = inner.x + (inner.width - spec.Width * scale) * .5f;
			var oy = inner.y + (inner.height - spec.Height * scale) * .5f;
			painter.fillColor = color;
			painter.BeginPath();
			for (var i = 0; i < ops.Count; i++)
			{
				var op = ops[i];
				if (op.Kind == Move) painter.MoveTo(Map(op.A, ox, oy, scale));
				else if (op.Kind == Line) painter.LineTo(Map(op.A, ox, oy, scale));
				else if (op.Kind == Cubic)
					painter.BezierCurveTo(Map(op.A, ox, oy, scale), Map(op.B, ox, oy, scale), Map(op.C, ox, oy, scale));
				else painter.ClosePath();
			}

			painter.Fill();
		}

		private static Vector2 Map(Vector2 point, float ox, float oy, float scale)
		{
			return new Vector2(ox + point.x * scale, oy + point.y * scale);
		}

		private static List<DrawOp> Compile(string path)
		{
			// Parse the SVG path once into drawing operations reused by later paints.
			var tokens = Tokenize(path);
			var ops = new List<DrawOp>();
			var index = 0;
			var command = '\0';
			float cx = 0f, cy = 0f, sx = 0f, sy = 0f;
			Vector2? cubic = null, quad = null;
			while (index < tokens.Count)
			{
				if (tokens[index].Command != '\0') command = tokens[index++].Command;
				if (command == '\0') break;
				var key = char.ToUpperInvariant(command);
				if (key == 'Z')
				{
					// Closing a subpath resets its current point and clears smooth-curve reflection state.
					ops.Add(new DrawOp { Kind = Close });
					cx = sx;
					cy = sy;
					cubic = quad = null;
					continue;
				}

				if (index >= tokens.Count || tokens[index].Command != '\0') break;
				var relative = char.IsLower(command);
				if (key == 'M')
				{
					// Additional coordinate pairs after a move are implicit line commands under SVG path rules.
					float x = Read(tokens, ref index), y = Read(tokens, ref index);
					if (relative)
					{
						x += cx;
						y += cy;
					}

					ops.Add(new DrawOp { Kind = Move, A = new Vector2(x, y) });
					cx = sx = x;
					cy = sy = y;
					cubic = quad = null;
					command = relative ? 'l' : 'L';
					continue;
				}

				if (key == 'L')
				{
					float x = Read(tokens, ref index), y = Read(tokens, ref index);
					if (relative)
					{
						x += cx;
						y += cy;
					}

					ops.Add(new DrawOp { Kind = Line, A = new Vector2(x, y) });
					cx = x;
					cy = y;
				}
				else if (key == 'H')
				{
					var x = Read(tokens, ref index);
					if (relative) x += cx;
					ops.Add(new DrawOp { Kind = Line, A = new Vector2(x, cy) });
					cx = x;
				}
				else if (key == 'V')
				{
					var y = Read(tokens, ref index);
					if (relative) y += cy;
					ops.Add(new DrawOp { Kind = Line, A = new Vector2(cx, y) });
					cy = y;
				}
				else if (key == 'C')
				{
					float x1 = Read(tokens, ref index),
						  y1 = Read(tokens, ref index),
						  x2 = Read(tokens, ref index),
						  y2 = Read(tokens, ref index),
						  x = Read(tokens, ref index),
						  y = Read(tokens, ref index);
					if (relative)
					{
						x1 += cx;
						y1 += cy;
						x2 += cx;
						y2 += cy;
						x += cx;
						y += cy;
					}

					ops.Add(new DrawOp
					{
						Kind = Cubic, A = new Vector2(x1, y1), B = new Vector2(x2, y2), C = new Vector2(x, y)
					});
					cubic = new Vector2(x2, y2);
					cx = x;
					cy = y;
				}
				else if (key == 'S')
				{
					// Reflect the prior cubic control point for a smooth continuation when one is available.
					float x2 = Read(tokens, ref index),
						  y2 = Read(tokens, ref index),
						  x = Read(tokens, ref index),
						  y = Read(tokens, ref index);
					if (relative)
					{
						x2 += cx;
						y2 += cy;
						x += cx;
						y += cy;
					}

					var x1 = cubic.HasValue ? 2f * cx - cubic.Value.x : cx;
					var y1 = cubic.HasValue ? 2f * cy - cubic.Value.y : cy;
					ops.Add(new DrawOp
					{
						Kind = Cubic, A = new Vector2(x1, y1), B = new Vector2(x2, y2), C = new Vector2(x, y)
					});
					cubic = new Vector2(x2, y2);
					cx = x;
					cy = y;
				}
				else if (key == 'Q')
				{
					float x1 = Read(tokens, ref index),
						  y1 = Read(tokens, ref index),
						  x = Read(tokens, ref index),
						  y = Read(tokens, ref index);
					if (relative)
					{
						x1 += cx;
						y1 += cy;
						x += cx;
						y += cy;
					}

					AddQuadratic(ops, new Vector2(cx, cy), new Vector2(x1, y1), new Vector2(x, y));
					quad = new Vector2(x1, y1);
					cx = x;
					cy = y;
				}
				else if (key == 'T')
				{
					// Reflect the preceding quadratic control point before converting the curve to cubic form.
					float x = Read(tokens, ref index), y = Read(tokens, ref index);
					if (relative)
					{
						x += cx;
						y += cy;
					}

					var x1 = quad.HasValue ? 2f * cx - quad.Value.x : cx;
					var y1 = quad.HasValue ? 2f * cy - quad.Value.y : cy;
					AddQuadratic(ops, new Vector2(cx, cy), new Vector2(x1, y1), new Vector2(x, y));
					quad = new Vector2(x1, y1);
					cx = x;
					cy = y;
				}
				else if (key == 'A')
				{
					float rx = Read(tokens, ref index),
						  ry = Read(tokens, ref index),
						  phi = Read(tokens, ref index),
						  fa = Read(tokens, ref index),
						  fs = Read(tokens, ref index),
						  x = Read(tokens, ref index),
						  y = Read(tokens, ref index);
					if (relative)
					{
						x += cx;
						y += cy;
					}

					AddArc(ops, new Vector2(cx, cy), rx, ry, phi, fa != 0f, fs != 0f, new Vector2(x, y));
					cx = x;
					cy = y;
				}

				if (key != 'C' && key != 'S') cubic = null;
				if (key != 'Q' && key != 'T') quad = null;
			}

			return ops;
		}

		private static void AddQuadratic(List<DrawOp> ops, Vector2 start, Vector2 control, Vector2 end)
		{
			// Convert a quadratic curve to equivalent cubic controls supported by Painter2D.
			var c1 = start + (control - start) * (2f / 3f);
			var c2 = end + (control - end) * (2f / 3f);
			ops.Add(new DrawOp { Kind = Cubic, A = c1, B = c2, C = end });
		}

		private static void AddArc(List<DrawOp> ops, Vector2 start, float rx, float ry, float phiDegrees, bool large,
								   bool sweep, Vector2 end)
		{
			if (Vector2.Distance(start, end) < 1e-4f) return;
			if (Mathf.Abs(rx) < 1e-4f || Mathf.Abs(ry) < 1e-4f)
			{
				ops.Add(new DrawOp { Kind = Line, A = end });
				return;
			}

			var phi = phiDegrees * Mathf.Deg2Rad;

			// Convert SVG endpoint coordinates into the rotated ellipse's coordinate frame.
			float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
			float dx = (start.x - end.x) * .5f, dy = (start.y - end.y) * .5f;
			var x1 = cos * dx + sin * dy;
			var y1 = -sin * dx + cos * dy;
			rx = Mathf.Abs(rx);
			ry = Mathf.Abs(ry);
			var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
			if (lambda > 1f)
			{
				// Expand undersized radii so the ellipse can reach both endpoints.
				var root = Mathf.Sqrt(lambda);
				rx *= root;
				ry *= root;
			}

			float rxsq = rx * rx, rysq = ry * ry, x1sq = x1 * x1, y1sq = y1 * y1;

			// Choose the ellipse center and signed sweep from the large-arc and sweep flags.
			var num = rxsq * rysq - rxsq * y1sq - rysq * x1sq;
			var den = rxsq * y1sq + rysq * x1sq;
			var coef = den == 0f ? 0f : Mathf.Sqrt(Mathf.Max(0f, num / den));
			if (large == sweep) coef = -coef;
			float cx1 = coef * rx * y1 / ry, cy1 = coef * -ry * x1 / rx;
			var centerX = cos * cx1 - sin * cy1 + (start.x + end.x) * .5f;
			var centerY = sin * cx1 + cos * cy1 + (start.y + end.y) * .5f;
			var theta1 = Angle(1f, 0f, (x1 - cx1) / rx, (y1 - cy1) / ry);
			var delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
			if (!sweep && delta > 0f) delta -= Mathf.PI * 2f;
			else if (sweep && delta < 0f) delta += Mathf.PI * 2f;
			var segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI * .5f) - 1e-4f));

			// Approximate the arc with cubic segments spanning at most a quarter turn each.
			var step = delta / segments;
			for (var i = 0; i < segments; i++)
			{
				var a0 = theta1 + step * i;
				var a1 = a0 + step;
				var alpha = Mathf.Tan(step * .5f) * 4f / 3f;
				var c1 = ArcPoint(centerX, centerY, rx, ry, cos, sin, a0, -alpha * Mathf.Sin(a0),
								  alpha * Mathf.Cos(a0));
				var c2 = ArcPoint(centerX, centerY, rx, ry, cos, sin, a1, alpha * Mathf.Sin(a1),
								  -alpha * Mathf.Cos(a1));
				var point = ArcPoint(centerX, centerY, rx, ry, cos, sin, a1, 0f, 0f);
				ops.Add(new DrawOp { Kind = Cubic, A = c1, B = c2, C = point });
			}
		}

		private static Vector2 ArcPoint(float centerX, float centerY, float rx, float ry, float cos, float sin,
										float theta, float dx, float dy)
		{
			var x = Mathf.Cos(theta) + dx;
			var y = Mathf.Sin(theta) + dy;
			return new Vector2(centerX + cos * rx * x - sin * ry * y, centerY + sin * rx * x + cos * ry * y);
		}

		private static float Angle(float ux, float uy, float vx, float vy)
		{
			// Clamp the dot product for numerical stability and recover orientation from the cross product.
			var length = Mathf.Sqrt(ux * ux + uy * uy) * Mathf.Sqrt(vx * vx + vy * vy);
			var value = length == 0f ? 1f : Mathf.Clamp((ux * vx + uy * vy) / length, -1f, 1f);
			var angle = Mathf.Acos(value);
			if (ux * vy - uy * vx < 0f) angle = -angle;
			return angle;
		}

		private static float Read(List<Token> tokens, ref int index)
		{
			return tokens[index++].Number;
		}

		private static List<Token> Tokenize(string path)
		{
			// Separate SVG command letters from signed decimal numbers, including scientific notation.
			var tokens = new List<Token>(path.Length / 2);
			var i = 0;
			while (i < path.Length)
			{
				var c = path[i];
				if (char.IsWhiteSpace(c) || c == ',')
				{
					i++;
					continue;
				}

				if (char.IsLetter(c))
				{
					tokens.Add(new Token { Command = c });
					i++;
					continue;
				}

				var start = i;
				if (c == '+' || c == '-') i++;
				if (i < path.Length && path[i] == '.')
				{
					i++;
					while (i < path.Length && char.IsDigit(path[i])) i++;
				}
				else
				{
					while (i < path.Length && char.IsDigit(path[i])) i++;
					if (i < path.Length && path[i] == '.')
					{
						i++;
						while (i < path.Length && char.IsDigit(path[i])) i++;
					}
				}

				if (i < path.Length && (path[i] == 'e' || path[i] == 'E'))
				{
					var exponent = i + 1;
					if (exponent < path.Length && (path[exponent] == '+' || path[exponent] == '-')) exponent++;
					if (exponent < path.Length && char.IsDigit(path[exponent]))
					{
						i = exponent + 1;
						while (i < path.Length && char.IsDigit(path[i])) i++;
					}
				}

				tokens.Add(new Token
				{
					Number = float.Parse(path.Substring(start, i - start), CultureInfo.InvariantCulture)
				});
			}

			return tokens;
		}

		private struct Icon
		{
			public float Width;
			public float Height;
			public string Path;
		}

		private struct DrawOp
		{
			public byte Kind;
			public Vector2 A;
			public Vector2 B;
			public Vector2 C;
		}

		private struct Token
		{
			public char Command;
			public float Number;
		}
	}
}
