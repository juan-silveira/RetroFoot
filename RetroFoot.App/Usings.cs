// os pincels sem dono de thread: um pincel criado fora da thread da interface (na do motor ou na
// do banco online) derrubava o jogo ao desenhar
global using ImmutableSolidColorBrush = Avalonia.Media.Immutable.ImmutableSolidColorBrush;
