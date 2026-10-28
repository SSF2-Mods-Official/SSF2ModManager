package
{
	import flash.display.Loader;
	import flash.display.Sprite;
	import flash.events.Event;
	import flash.events.IOErrorEvent;
	import flash.events.KeyboardEvent;
	import flash.filesystem.File;
	import flash.filesystem.FileMode;
	import flash.filesystem.FileStream;
	import flash.media.Sound;
	import flash.media.SoundChannel;
	import flash.net.URLRequest;
	import flash.system.ApplicationDomain;
	import flash.system.LoaderContext;
	import flash.text.TextField;
	import flash.text.TextFormat;
	import flash.desktop.NativeApplication;
	import flash.ui.Keyboard;

	/**
	 * Tiny AIR app: load a bgm_*.swf from disk and play its Sound export.
	 * Reads preview.json next to application.xml (written by the extension).
	 */
	public class BgmPreview extends Sprite
	{
		private var m_label:TextField;
		private var m_channel:SoundChannel;
		private var m_loader:Loader;

		public function BgmPreview()
		{
			super();
			m_label = new TextField();
			m_label.defaultTextFormat = new TextFormat("Segoe UI", 14, 0xffffff);
			m_label.width = 480;
			m_label.height = 120;
			m_label.x = 16;
			m_label.y = 16;
			m_label.multiline = true;
			m_label.wordWrap = true;
			m_label.text = "BGM Preview…";
			addChild(m_label);
			stage.color = 0x111111;
			stage.addEventListener(KeyboardEvent.KEY_DOWN, onKey);
			boot();
		}

		private function boot():void
		{
			var data:Object;
			var stream:FileStream;
			var file:File;
			var text:String;
			try
			{
				file = File.applicationDirectory.resolvePath("preview.json");
				if (!file.exists)
				{
					fail("Missing preview.json");
					return;
				};
				stream = new FileStream();
				stream.open(file, FileMode.READ);
				text = stream.readUTFBytes(stream.bytesAvailable);
				stream.close();
				data = JSON.parse(text);
			}
			catch (err:*)
			{
				fail("Cannot read preview.json: " + err);
				return;
			};
			m_label.text = "Loading " + String(data.id || "") + "…";
			loadSwf(String(data.swfPath || ""), String(data.id || ""));
		}

		private function loadSwf(swfPath:String, id:String):void
		{
			var ctx:LoaderContext;
			if (!swfPath)
			{
				fail("Missing swfPath");
				return;
			};
			m_loader = new Loader();
			ctx = new LoaderContext(false, new ApplicationDomain(ApplicationDomain.currentDomain));
			ctx.allowCodeImport = true;
			m_loader.contentLoaderInfo.addEventListener(Event.COMPLETE, function (e:Event):void
			{
				playFromDomain(m_loader.contentLoaderInfo.applicationDomain, id);
			});
			m_loader.contentLoaderInfo.addEventListener(IOErrorEvent.IO_ERROR, function (e:IOErrorEvent):void
			{
				fail("Load failed: " + e.text);
			});
			try
			{
				m_loader.load(new URLRequest(new File(swfPath).url), ctx);
			}
			catch (e2:*)
			{
				fail("Load error: " + e2);
			};
		}

		private function playFromDomain(domain:ApplicationDomain, id:String):void
		{
			var cls:Class;
			var sound:Sound;
			var names:Array;
			var i:int;
			var n:String;
			var all:Vector.<String>;
			var qn:String;
			var inst:*;
			// Flex-exported music SWFs use Main_<id> (e.g. Main_bgm_alcatraz).
			names = [
				"Main_" + id,
				id,
				id + "_sound",
				"MainTimeline_" + id,
				"music",
				"bgm"
			];
			i = 0;
			while (i < names.length)
			{
				n = names[i];
				sound = tryMakeSound(domain, n);
				if (sound)
				{
					break;
				};
				i++;
			};
			if (!sound)
			{
				try
				{
					all = domain.getQualifiedDefinitionNames();
					i = 0;
					while (i < all.length)
					{
						qn = all[i];
						if (qn && (qn.indexOf(id) >= 0 || qn.indexOf("bgm_") >= 0 || qn.indexOf("Sound") >= 0))
						{
							sound = tryMakeSound(domain, qn);
							if (sound)
							{
								break;
							};
						};
						i++;
					};
					// Last resort: first Sound/SoundAsset subclass in the SWF
					if (!sound)
					{
						i = 0;
						while (i < all.length)
						{
							sound = tryMakeSound(domain, all[i]);
							if (sound)
							{
								break;
							};
							i++;
						};
					};
				}
				catch (scanErr:*)
				{
					trace("[bgm-preview] scan: " + scanErr);
				};
			};
			if (!sound)
			{
				fail("No Sound export found for " + id + " (tried Main_" + id + ")");
				return;
			};
			m_label.text = "Playing " + id + "\nEsc to stop";
			try
			{
				m_channel = sound.play(0, 0);
				if (m_channel)
				{
					m_channel.addEventListener(Event.SOUND_COMPLETE, function (e:Event):void
					{
						NativeApplication.nativeApplication.exit(0);
					});
				};
			}
			catch (playErr:*)
			{
				fail("Play failed: " + playErr);
			};
		}

		private function tryMakeSound(domain:ApplicationDomain, name:String):Sound
		{
			var cls:Class;
			var inst:*;
			if (!name || !domain.hasDefinition(name))
			{
				return (null);
			};
			try
			{
				cls = domain.getDefinition(name) as Class;
				inst = new cls();
				if (inst is Sound)
				{
					return (inst as Sound);
				};
			}
			catch (err:*)
			{
			};
			return (null);
		}

		private function onKey(e:KeyboardEvent):void
		{
			if (e.keyCode == Keyboard.ESCAPE)
			{
				stopAndExit();
			};
		}

		private function stopAndExit():void
		{
			try
			{
				if (m_channel)
				{
					m_channel.stop();
				};
			}
			catch (err:*)
			{
			};
			NativeApplication.nativeApplication.exit(0);
		}

		private function fail(msg:String):void
		{
			m_label.text = msg;
			trace("[bgm-preview] " + msg);
		}
	}
}
