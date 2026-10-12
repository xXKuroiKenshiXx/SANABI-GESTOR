"""Source migration for resource-based themes and interface translations."""
from pathlib import Path
import re, json, html
ROOT=Path(__file__).resolve().parents[1]
app=ROOT/'gestor/App.axaml'
s=app.read_text('utf-8')
if 'ResourceDictionary.ThemeDictionaries' in s: raise SystemExit('UI already migrated; edit the current resources directly.')
s=s.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"','xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n             xmlns:views="using:SanabiGestor.Views"')
brushes=re.search(r'  <Application.Resources>(.*?)  </Application.Resources>',s,re.S).group(1)
light={'BgBrush':'#F5F3FC','PanelBrush':'#EEEBF7','CardBrush':'#FFFFFF','CardHoverBrush':'#F0EBFD','LineBrush':'#D8D1E8','AccentBrush':'#C82C55','AccentSoftBrush':'#206B50A5','VioletBrush':'#6841AF','TextBrush':'#231B35','MutedBrush':'#655A78','OkBrush':'#157953','WarnBrush':'#925C08','CyanBrush':'#08798C'}
lb=brushes
for k,v in light.items(): lb=re.sub(r'(x:Key="'+k+r'" Color=")[^"]+',lambda m:m[1]+v,lb)
s=re.sub(r'  <Application.Resources>.*?  </Application.Resources>','''  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Dark">'''+brushes+'''        </ResourceDictionary>
        <ResourceDictionary x:Key="Light">'''+lb+'''        </ResourceDictionary>
      </ResourceDictionary.ThemeDictionaries>
      <DataTemplate x:Key="ActionTemplate" x:CompileBindings="False"><views:ActionLabel Text="{Binding}" /></DataTemplate>
    </ResourceDictionary>
  </Application.Resources>''',s,flags=re.S)
s=s.replace('<FluentTheme.Palettes>','<FluentTheme.Palettes>\n        <ColorPaletteResources x:Key="Light" Accent="#C82C55" RegionColor="#F5F3FC" />')
s=s.replace('<Style Selector="Button">','<Style Selector="Button">\n      <Setter Property="ContentTemplate" Value="{StaticResource ActionTemplate}" />',1)
s=s.replace('<Style Selector="Button.cardTitle">','<Style Selector="Button.cardTitle">\n      <Setter Property="ContentTemplate" Value="{x:Null}" />')
s=s.replace('Background" Value="#262B38"','Background" Value="{DynamicResource PanelBrush}"')
s=s.replace('{StaticResource ', '{DynamicResource ')
# Templates are ordinary resources, not theme brushes.
s=s.replace('{DynamicResource ActionTemplate}', '{StaticResource ActionTemplate}')
app.write_text(s,'utf-8')
window=ROOT/'gestor/Views/MainWindow.axaml'
s=window.read_text('utf-8')
s=s.replace('xmlns:views="using:SanabiGestor.Views"','xmlns:views="using:SanabiGestor.Views"\n        xmlns:loc="using:SanabiGestor.Core"')
s=s.replace('{StaticResource ', '{DynamicResource ')
s=s.replace('{DynamicResource ModCardTemplate}', '{StaticResource ModCardTemplate}')
s=s.replace('<ListBoxItem Content="Ajustes" />','<ListBoxItem Content="Ajustes" />\n            <ListBoxItem Content="Comunidad Discord" />')
s=s.replace('<ListBox Classes="nav" SelectedIndex="{Binding Section}">','<ListBox Classes="nav" SelectedIndex="{Binding Section}">\n            <ListBox.Styles><Style Selector="ListBoxItem"><Setter Property="ContentTemplate" Value="{StaticResource ActionTemplate}" /></Style></ListBox.Styles>')
s=re.sub(r'<Border Classes="card" IsVisible="\{Binding IsTranslation\}"><StackPanel Spacing="10">.*?</StackPanel></Border>', '''<Border Classes="card" IsVisible="{Binding IsTranslation}"><StackPanel Spacing="10">
                <TextBlock Classes="h2" Text="Traducción por edición" />
                <TextBlock Classes="body" Text="Elegí la fecha de la traducción y pulsá Instalar. El gestor descarga los .bundle correspondientes del repositorio y los instala con copia de respaldo. No traduce durante el juego." />
              </StackPanel></Border>''',s,flags=re.S)
s=s.replace('<!-- Ajustes -->','''<!-- Discord -->
        <ScrollViewer IsVisible="{Binding IsDiscordSection}">
          <StackPanel Margin="28,24,28,28" Spacing="16" MaxWidth="900" HorizontalAlignment="Left">
            <TextBlock Classes="h1" Text="Comunidad Discord" />
            <Border Classes="card"><StackPanel Spacing="12">
              <TextBlock Classes="body" Text="Mods publicados por la comunidad en el foro de Discord." />
              <TextBlock Classes="muted" Text="Canal: 1558909352146895030" />
              <Button Classes="primary" Content="Abrir Discord" Command="{Binding OpenDiscordCommand}" HorizontalAlignment="Left" />
            </StackPanel></Border>
            <Border Classes="card"><StackPanel Spacing="12">
              <TextBlock Classes="h2" Text="Catálogo automático" />
              <TextBlock Classes="body" Text="GitHub Actions puede leer el foro con un bot autorizado y publicar el catálogo. El gestor consulta ese catálogo sin guardar el token del bot. No necesitás mantener un servidor encendido." />
              <TextBox Text="{Binding DiscordCatalogUrl}" Watermark="https://…/discord-catalog.json" />
              <Button Classes="ghost" Content="Guardar y actualizar" Command="{Binding LoadDiscordCommand}" HorizontalAlignment="Left" />
              <TextBlock Classes="muted" Text="{Binding DiscordStatus}" />
            </StackPanel></Border>
            <ItemsControl ItemsSource="{Binding DiscordMods}" ItemTemplate="{StaticResource ModCardTemplate}">
              <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
            </ItemsControl>
          </StackPanel>
        </ScrollViewer>

        <!-- Ajustes -->''')
s=s.replace('<TextBlock Classes="h1" Text="Ajustes" />','''<TextBlock Classes="h1" Text="Ajustes" />
            <Border Classes="card"><StackPanel Spacing="12">
              <TextBlock Classes="h2" Text="Apariencia e idioma" />
              <Grid ColumnDefinitions="*,20,*">
                <StackPanel Spacing="6"><TextBlock Classes="muted" Text="Tema" /><ComboBox ItemsSource="{Binding Themes}" SelectedItem="{Binding SelectedTheme}" HorizontalAlignment="Stretch" /></StackPanel>
                <StackPanel Grid.Column="2" Spacing="6"><TextBlock Classes="muted" Text="Idioma de la aplicación" /><ComboBox ItemsSource="{Binding Languages}" SelectedItem="{Binding SelectedLanguage}" HorizontalAlignment="Stretch" /></StackPanel>
              </Grid>
              <TextBlock Classes="muted" Text="Los cambios se aplican al instante. El idioma del juego se elige en el mod de traducción." />
            </StackPanel></Border>''')
keys=set()
# ActionLabel handles button/nav text without changing command content.
for m in re.finditer(r'<(?:Button|ListBoxItem)\b[^>]*\bContent="([^"]+)"',s):
    t=html.unescape(m[1]);
    if t and not t.startswith('{'): keys.add(t)
def localize(m):
    before,attr,t=m.group(1),m.group(2),m.group(3)
    decoded=html.unescape(t)
    if not decoded or decoded.startswith('{') or decoded in {'SANABI','✓','!'}: return m[0]
    keys.add(decoded)
    return before+attr+'="{loc:Tr '+t+'}"'
s=re.sub(r'(<(?:TextBlock|SelectableTextBlock|Run|TextBox)\b[^>]*?\b)(Text|Watermark)="([^"]*)"',localize,s)
window.write_text(s,'utf-8')
keys.update(['Instalar','Reinstalar','Actualizar','Instalado','Ver como usarlo','Abrir','Sin revisar','Es de SANABI','Ver archivos en Nexus','Cancelar','Aceptar','Sí','No','Desinstalar','Traduccion','Jugabilidad','Personaje','Audio','Visual','Dificultad','Herramienta','Otros','Catalogo','Archivo local','Listo.','Juego no seleccionado','Elegí un catálogo HTTPS para conectar el foro.','Catálogo de Discord actualizado.'])
d=ROOT/'gestor/Assets/Languages'; d.mkdir(exist_ok=True)
(d/'es.json').write_text(json.dumps({k:k for k in sorted(keys)},ensure_ascii=False,indent=2),'utf-8')
for lang in ('en','zh','ru'):
    (d/f'{lang}.json').write_text('{}','utf-8')
print(json.dumps(sorted(keys),ensure_ascii=False,indent=2))
